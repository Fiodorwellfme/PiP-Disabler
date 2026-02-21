using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    internal static class StabilizedState
    {
        private static bool _stabilizeInit;
        private static Vector3 _lensCamSmoothed;
        private static Quaternion _opticRotSmoothed = Quaternion.identity;
        private static float _lensDeltaCam;

        public static void Reset()
        {
            _stabilizeInit = false;
            _lensCamSmoothed = Vector3.zero;
            _opticRotSmoothed = Quaternion.identity;
            _lensDeltaCam = 0f;
        }

        public static void UpdateStabilized(Camera fpsCam, Transform lensTf, Transform opticTf)
        {
            if (fpsCam == null || lensTf == null || opticTf == null) return;

            Vector3 lensCam = fpsCam.transform.InverseTransformPoint(lensTf.position);

            const float tauPos = 0.05f;
            float dt = Mathf.Max(0.000001f, Time.unscaledDeltaTime);
            float aPos = 1f - Mathf.Exp(-dt / tauPos);

            if (!_stabilizeInit)
            {
                _stabilizeInit = true;
                _lensCamSmoothed = lensCam;
                _opticRotSmoothed = opticTf.rotation;
                _lensDeltaCam = 0f;
                return;
            }

            const float deadzone = 0.0005f;
            Vector3 d = lensCam - _lensCamSmoothed;
            _lensDeltaCam = d.magnitude;

            if (d.sqrMagnitude > deadzone * deadzone)
                _lensCamSmoothed = Vector3.Lerp(_lensCamSmoothed, lensCam, aPos);

            // Keep optic rotation frame-accurate (no smoothing) to avoid visible reticle follow-lag.
            // The stabilization win comes from LateUpdate timing + camera-space lens smoothing.
            _opticRotSmoothed = opticTf.rotation;
        }

        public static bool IsInitialized => _stabilizeInit;
        public static Vector3 LensCamSmoothed => _lensCamSmoothed;
        public static Quaternion OpticRotSmoothed => _opticRotSmoothed;
        public static float LensDeltaCam => _lensDeltaCam;
    }
}
