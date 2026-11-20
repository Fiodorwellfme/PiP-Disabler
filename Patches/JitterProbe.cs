using System;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Diagnostic (2.8.13, Debug logging only since 2.8.16): how much each signal jumps per frame while
    /// aiming, in screen pixels at the current zoom. One line every 2 s while zoomed in (FOV &lt; 20°). "Jitter" = the second difference of a signal (frame-to-frame
    /// change of its frame-to-frame motion): smooth sway gives ~0, a one-frame jump gives a spike.
    /// Signals: scene = final camera direction (what you see move), roll = camera roll at 500 px from the
    /// centre, eft = EFT's own camera, optic = the optic camera (what the camera follows with "Keep scope
    /// centered" off), axis = scope bone axis (followed with it on), scope = scope bone on screen
    /// (housing), reticle = reticle aim point on screen, hole = pixel hole centre, dt = frame time.
    /// </summary>
    internal static class JitterProbe
    {
        private sealed class Sig
        {
            private Vector3 _p1, _p2;
            private int _have;
            internal int N;
            internal double Sum2;
            internal float Max;

            internal void Add(Vector3 v, float scale)
            {
                if (_have >= 2)
                {
                    float d = (v - 2f * _p1 + _p2).magnitude * scale;
                    Sum2 += d * d; N++;
                    if (d > Max) Max = d;
                }
                _p2 = _p1; _p1 = v;
                if (_have < 2) _have++;
            }

            internal void Restart() { _have = 0; }
            internal void Clear() { N = 0; Sum2 = 0; Max = 0; }
            internal string Text => N == 0 ? "-" : $"{Math.Sqrt(Sum2 / N):F2}/{Max:F1}";
        }

        private static readonly Sig Scene = new Sig(), Roll = new Sig(), Eft = new Sig(), Optic = new Sig(),
            Axis = new Sig(), Scope = new Sig(), Reticle = new Sig(), HoleSig = new Sig();
        private static int _lastFrame = -10, _holeFrame = -10, _frames, _skipped;
        private static float _windowStart, _dtSum, _dtSum2, _dtMax, _tiltSum;

        private static int _repeats;
        private static readonly System.Collections.Generic.Dictionary<string, int> _paths = new System.Collections.Generic.Dictionary<string, int>();

        /// <summary>The alignment was asked for again in the same frame (2.8.14: only re-applied).</summary>
        internal static void RepeatCall(float angleDeg) { if (On) _repeats++; }

        private static bool On => Settings.DebugLogging != null && Settings.DebugLogging.Value;

        internal static void Frame(Camera cam, Quaternion eftRot, Quaternion opticRot, Vector3 zeroDir, bool fireOrReload, string path)
        {
            if (!On) return;
            try
            {
                if (cam == null) return;
                float fov = cam.fieldOfView;
                int f = Time.frameCount;
                if (fov >= 20f || fireOrReload || f != _lastFrame + 1)
                {
                    if (f != _lastFrame) RestartAll();
                    _lastFrame = f;
                    if (fov >= 20f || fireOrReload) { _skipped++; return; }
                }
                _lastFrame = f;
                _paths.TryGetValue(path ?? "-", out int pc);
                _paths[path ?? "-"] = pc + 1;

                float pxPerDeg = Screen.height / Mathf.Max(fov, 0.01f);
                float pxPerRad = pxPerDeg * Mathf.Rad2Deg;
                Transform ct = cam.transform;
                Scene.Add(ct.forward, pxPerRad);
                Roll.Add(ct.up, 500f);
                Eft.Add(eftRot * Vector3.forward, pxPerRad);
                Optic.Add(opticRot * Vector3.forward, pxPerRad);

                var bone = Helpers.GetLocalPlayer()?.ProceduralWeaponAnimation?.CurrentScope?.Bone;
                if (bone != null)
                {
                    Vector3 axis = bone.name == "aim_camera" ? -bone.up : -bone.forward;
                    Axis.Add(axis.normalized, pxPerRad);
                    Vector3 sp = cam.WorldToScreenPoint(bone.position);
                    Scope.Add(new Vector3(sp.x, sp.y, 0f), 1f);
                    _tiltSum += Vector3.Angle(axis, zeroDir);
                }
                Vector3 rp = cam.WorldToScreenPoint(ct.position + zeroDir * 100f);
                Reticle.Add(new Vector3(rp.x, rp.y, 0f), 1f);

                float dt = Time.unscaledDeltaTime * 1000f;
                _dtSum += dt; _dtSum2 += dt * dt; if (dt > _dtMax) _dtMax = dt;
                _frames++;

                float now = Time.realtimeSinceStartup;
                if (_frames == 1) _windowStart = now;
                if (now - _windowStart >= 2f && _frames >= 30) Flush(fov);
            }
            catch { }
        }

        internal static void Hole(Vector2 sc)
        {
            if (!On) return;
            int f = Time.frameCount;
            if (f != _holeFrame + 1) HoleSig.Restart();
            _holeFrame = f;
            HoleSig.Add(new Vector3(sc.x, sc.y, 0f), 1f);
        }

        private static string Paths()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _paths) sb.Append(sb.Length == 0 ? "" : ",").Append(kv.Key).Append('=').Append(kv.Value);
            return sb.ToString();
        }

        private static void RestartAll()
        {
            Scene.Restart(); Roll.Restart(); Eft.Restart(); Optic.Restart();
            Axis.Restart(); Scope.Restart(); Reticle.Restart(); HoleSig.Restart();
        }

        private static void Flush(float fov)
        {
            float mean = _dtSum / _frames;
            float sd = Mathf.Sqrt(Mathf.Max(0f, _dtSum2 / _frames - mean * mean));
            bool keep = Settings.KeepScopeCenteredWithZeroing != null && Settings.KeepScopeCenteredWithZeroing.Value;
            PiPDisablerPlugin.LogSource.LogInfo(
                $"[Jitter] keepCentered={(keep ? "on" : "off")} fov={fov:F2} frames={_frames} fps={1000f / mean:F0} dt max={_dtMax:F1}ms sd={sd:F1}ms" +
                $" | px per frame (rms/max): scene {Scene.Text} roll {Roll.Text} eft {Eft.Text} optic {Optic.Text} axis {Axis.Text}" +
                $" scope {Scope.Text} reticle {Reticle.Text} hole {HoleSig.Text} | zero tilt {_tiltSum / _frames:F3}° skipped {_skipped} repeat calls {_repeats} paths {Paths()}");
            foreach (var s in new[] { Scene, Roll, Eft, Optic, Axis, Scope, Reticle, HoleSig }) s.Clear();
            _frames = 0; _skipped = 0; _repeats = 0; _paths.Clear(); _dtSum = _dtSum2 = _dtMax = 0f; _tiltSum = 0f;
        }
    }
}
