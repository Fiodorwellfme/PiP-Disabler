# Ribcage / FOV Compensation in EFT (SPT) — How it Works, Where It Bites, and How to Mod It

This document explains the **ribcage/FOV compensation system** used in Escape from Tarkov (and therefore in SPT builds based on EFT), including:
- what the system is trying to preserve visually,
- which subsystems it touches (rig scale, aim math, recoil, shot vectors),
- why naïve weapon scaling causes drift/jitter/flicker,
- and how to safely replace or extend the behavior in a mod (Harmony/BepInEx).

> Note: Some of the source files you uploaded earlier may expire from this workspace over time. This write-up is based on the mechanisms we discussed from `Player` + `ProceduralWeaponAnimation`. If you want a version that includes exact method signatures and patch points for your current repo, re-upload the current versions of the relevant files.

---

## 1) What “ribcage/FOV compensation” is trying to achieve

EFT has two competing realities:

1) **World camera FOV** changes the projection (what looks big/small on screen).
2) The **first-person viewmodel** (hands/weapon) is not “just another world object” for the player:
   - players expect the weapon to sit in a consistent place,
   - sights should line up consistently,
   - recoil should feel consistent,
   - and bullets should go where the sight picture indicates.

So EFT uses a compensation system that:
- **applies a non-uniform scale** to a first-person rig anchor (the “ribcage” / first-person skeleton chain),
- **feeds the same scale into procedural aiming math** (so aim transforms are computed in the same distorted space),
- **adjusts recoil math** under compensation,
- **and conditionally corrects shot vectors** (position + direction) so ballistics still matches the compensated rig.

It’s not “just scaling the gun.” It’s a *coupled* system.

---

## 2) The two different “goals” people mix up

### Goal A — Vanilla-style “base FOV compensation”
Vanilla mainly compensates for the *player’s configured base FOV*, so the viewmodel doesn’t feel too tiny at high base FOV.

This is usually a monotonic curve:
- higher base FOV ⇒ more compensation (viewmodel gets “compressed” along one axis)
- and lots of logic assumes the compensated value is **≤ 1**.

### Goal B — Constant screen-space size across dynamic FOV zoom (your goal)
You want:
- smaller FOV (zoom in) ⇒ **weapon should shrink** so it doesn’t balloon on screen,
- larger FOV (zoom out) ⇒ **weapon should grow** so it doesn’t look tiny.

That’s the opposite direction from the common “make it bigger at low FOV to feel nice” approach.

This distinction matters because some EFT logic is gated behind checks like `scale < 1`.

---

## 3) Where the compensation value comes from

### 3.1 Vanilla curve (typical EFT behavior)
A typical vanilla curve maps a FOV range to a scale range (example we discussed):
- normalize `t = inverseLerp(50, 75, fov)`
- `scale = lerp(1.0, 0.65, t)`

This yields **scale ≤ 1** for “normal” FOVs, which is convenient because other systems only “correct” when scale < 1.

### 3.2 Projection-correct curve (for constant screen size)
If you want the viewmodel to remain the same apparent size on screen across FOV changes, use the camera projection relationship:

Apparent size on screen is proportional to:
`1 / tan(FOV/2)`

So cancel it out by scaling with:
`tan(FOV/2)`

**Recommended formula:**

```csharp
scale(currentFov) = baselineScale * tan(currentFov/2) / tan(referenceFov/2)
```

Properties:
- FOV decreases ⇒ tan(FOV/2) decreases ⇒ scale decreases (weapon shrinks)
- FOV increases ⇒ tan(FOV/2) increases ⇒ scale increases (weapon grows)

**Pick `referenceFov` carefully:**
- if you want “weapon looks normal at hipfire base FOV” ⇒ reference = base hipfire FOV
- if you want “weapon looks normal at 1x ADS FOV” ⇒ reference = lowest-magnification ADS FOV

---

## 4) Where the scale is applied (and why it’s non-uniform)

### 4.1 The “ribcage” anchor
EFT tracks something like:
- `RibcageScaleCurrent`
- `RibcageScaleCurrentTarget`

The ribcage transform (first-person rig) ends up getting a **non-uniform** `localScale`, not a uniform one.

Why non-uniform?
- Uniform scaling changes everything equally and tends to break “weapon sits in hands” relationships in ways EFT doesn’t want.
- Non-uniform scaling acts more like a “depth/axis compensation” for how the viewmodel is projected and posed.

### 4.2 Axis confusion: “is it Y or Z?”
You’ll see the compensated axis appear as **Y in one place** and **Z in another**, because:
- different parts of the system work in different coordinate spaces (camera parent space, hands hierarchy local space, weapon root parent space),
- and “forward/depth” can map to different axes after transforms.

The correct mental model is:
- **there is ONE compensated axis**, but it looks like different components depending on which space you’re in.

**Practical rule:** do not “guess” the axis. Verify by:
- logging/visualizing local vectors in the relevant transform space,
- and confirming that the same axis is used consistently in:
  - the rig scaling,
  - the procedural aim TRS scale vector,
  - and the shot vector correction.

---

## 5) How ProceduralWeaponAnimation participates

This is the most important pitfall area: many mods scale the rig but forget to update PWA.

### 5.1 PWA uses the scale in its aim-space math
PWA stores a compensatory value (often `_compensatoryScale`) and uses it to build matrices like:
- `Matrix4x4.TRS(position, rotation, scaleVector)`

That scale vector is usually something like `(1, scale, 1)` (in *that* local space).

It then uses these matrices to:
- convert between camera-relative and weapon-relative aim points,
- compute aiming plane values and depth offsets,
- stabilize sight picture behavior under compensation.

If you scale the rig but don’t feed the same value into PWA’s math, you’ll often see:
- reticle jitter,
- “micro snapping” when aiming,
- optic alignment drifting as animations blend,
- inconsistent recoil arcs.

### 5.2 PWA also adjusts recoil angles under compensation
When compensation is active, recoil angles may be corrected with a relationship like:
- `angle' = atan(tan(angle) * scale)`

This keeps recoil “looking” consistent after your aim space is distorted.

If you bypass PWA or half-apply compensation:
- recoil can look too strong/weak in one axis,
- or it can introduce cyclic jitter as PWA fights your scaling.

### 5.3 Calibration runs more aggressively under compensation
PWA tends to re-calibrate more often while:
- aiming,
- and compensated scale is active.

That’s intentional: you’re effectively operating in a warped aim space, and calibration errors show up quickly.

If your mod toggles scale rapidly or changes FOV mid-frame:
- you can force constant recalibration and get flicker or unstable reticle.

---

## 6) Shot vector correction (why bullets drift if you skip it)

Scaling the first-person rig can implicitly distort:
- muzzle position relative to camera,
- and direction vectors computed from viewmodel transforms.

EFT’s approach is:
1) transform shot `position` and `direction` into **hands hierarchy local space**
2) scale the compensated axis component by `RibcageScaleCurrent`
3) transform back to world space

There is usually a gating flag (commonly named like `ShotNeedsFovAdjustments`) because:
- during some optic calibration paths, applying that correction would double-compensate or destabilize.

### What goes wrong if you do it wrong
- **No correction:** point-of-impact drift, especially noticeable at short range.
- **Wrong axis:** drift that changes with weapon pose or stance.
- **Always-on correction:** jitter or “wobble” during optic transitions where EFT expected it disabled.
- **Mismatch between Current vs Target:** shots briefly drift during scale transitions.

**Recommendation:**
- Correct shot vectors using the *same* “current applied scale” used for the rig at that moment (not the target).
- Apply correction only when the engine’s own gating conditions imply it’s safe.

---

## 7) The “multiple writers” pitfall (flicker / fast oscillation)

The most common mod failure mode is **two systems writing scale each frame**:

- vanilla code updates ribcage target from base FOV,
- your mod updates target from dynamic FOV,
- another mod updates PWA parameters,
- plus animation code is smoothing current->target.

Symptoms:
- very fast flicker,
- constant “fight” between two target values,
- reticle jitter that looks like high-frequency vibration.

### Fix: enforce a single authority
While your feature is enabled (e.g., while aiming or while scoped):
- either **disable** the vanilla scale computation (Harmony prefix return false),
- or override its inputs so it returns your value,
- or patch at a lower level where only one path writes the final value.

Then:
- restore vanilla behavior when your mode is off.

---

## 8) Smoothing: what to smooth, and what NOT to smooth

You typically have:
- a target scale (computed from FOV),
- a current scale (applied to the rig / used for shot correction).

### Good smoothing
- Smooth the scale value with a critically damped spring or exponential smoothing.
- Use the same smoothed value for:
  - rig scale,
  - PWA set-params,
  - shot vector correction.

### Bad smoothing
- Smooth rig scale, but feed *unsmoothed* scale into PWA (or vice versa).
- Smooth target, but shot correction uses target while rig uses current.
- Update scale in multiple update loops (Update + LateUpdate + coroutine), creating phase lag.

**Rule:** use one “authoritative current scale” for everything per frame.

---

## 9) How to implement constant weapon size across FOV safely

### 9.1 Choose your control mode
Common options:
- **Only when FOV zoom is active**: weapon scaling is tied to dynamic FOV changes.
- **Always**: affects hipfire, ADS, and all camera adjustments (usually not desired).

### 9.2 Capture a reference FOV
At the moment you consider “baseline appearance,” store:
- `referenceFov` (float)
- `baselineScale` (usually 1)

Then compute:
`scale = baselineScale * tan(currentFov/2) / tan(referenceFov/2)`

### 9.3 Apply scale + feed PWA
Each frame (or each zoom step):
- compute `targetScale`
- smooth -> `currentScale`
- apply to ribcage (one axis)
- call PWA’s setter that updates internal parameters (e.g., `SetFovParams(currentScale)`)

### 9.4 Patch shot vectors (conditionally)
Hook the place EFT finalizes shot origin + direction and apply:
- transform to hands local,
- scale the compensated axis component by `currentScale`,
- transform back.

Respect any gating flag that indicates “do not adjust now.”

---

## 10) What to patch (Harmony/BepInEx strategy)

With Harmony + BepInEx, you normally pick one of these:

### Strategy A — Override “compute scale” method
Patch the method that computes the compensated scale from FOV and return your value.
Pros:
- central and clean.
Cons:
- you must ensure it’s called for your zoom context, not just base FOV.

### Strategy B — Override “apply scale” method
Patch the method that writes the ribcage scale each frame.
Pros:
- you become the single writer.
Cons:
- more fragile across EFT versions if the application code moves.

### Strategy C — Intercept dynamic FOV zoom code and drive scale yourself
Patch your zoom controller:
- when you set camera FOV, also set ribcage/PWA scale.
Pros:
- best match to “only when zooming”
Cons:
- must ensure vanilla does not also write a different target.

**In all cases:** make sure only one path sets the value while enabled.

---

## 11) Edge cases & pitfalls checklist

### 11.1 Variable zoom / multi-mode optics
- Don’t assume a single scope FOV.
- Your scale must update whenever zoom changes (scroll wheel, mode switch).

### 11.2 Scope transitions (enter/exit ADS)
- Capture reference FOV at a stable moment (e.g., when entering ADS at 1x).
- Restore scale cleanly on scope exit (including quick-cancel).

### 11.3 Third-person or special cameras
Vanilla typically resets the ribcage scale when leaving first-person.
If you don’t:
- you can distort third-person animations or spectator views.

### 11.4 Performance
Avoid:
- allocations in per-frame code,
- repeated `GetComponent` calls,
- repeated transform searches.
Cache:
- ribcage transform,
- hands hierarchy root,
- camera references.

### 11.5 Network/headless quirks (SPT/Fika)
If simulation cadence differs (server/headless vs client render):
- ensure your scale driver runs on the render client in a stable update phase (often LateUpdate).
- avoid coupling shot correction to render-only events if shots are simulated elsewhere.

### 11.6 Map-dependent jitter
Large maps can amplify floating point / camera precision issues when combined with heavy per-frame TRS math.
Mitigations:
- keep compensation stable (don’t toggle rapidly),
- smooth transitions,
- avoid recomputing deep aim matrices multiple times per frame.

---

## 12) Debugging playbook

### Symptom: reticle jitters only on big maps
- confirm you don’t have multiple writers,
- confirm scale is applied in LateUpdate consistently,
- confirm PWA uses the same scale you apply to the rig.

### Symptom: point of impact is off at short range
- verify shot vector correction is applied,
- verify you’re scaling the correct axis in hands local space,
- verify you’re using *current* applied scale, not target.

### Symptom: fast flicker
- you almost certainly have two systems fighting (vanilla + mod).
- log target/current scale each frame and identify who writes it.

### Symptom: recoil feels wrong
- PWA recoil correction may not be receiving your scale.
- ensure you call the correct “set fov params / compensatory scale” method.

---

## 13) Recommended implementation defaults

1) **Drive scale only while scoped/zooming** (your use case).
2) **Use projection-correct scale**: `tan(current/2)/tan(ref/2)`.
3) **One authoritative current scale** per frame used for:
   - ribcage transform scaling,
   - PWA parameters,
   - shot vector correction.
4) **Disable vanilla writer** while enabled.
5) **Respect shot-adjust gating** (don’t “always adjust”).

---

## 14) If you re-upload: what I can turn this into
If you re-upload the current:
- `Player` class (or the part that computes/applies ribcage scaling),
- `ProceduralWeaponAnimation`,
- your zoom controller / your current patch file,

…I can produce:
- an exact “patch plan” with specific Harmony prefixes/postfixes,
- the precise axis mapping for your chosen transforms,
- and a drop-in implementation that avoids flicker and shot drift.
