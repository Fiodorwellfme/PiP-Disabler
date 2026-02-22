using System;

namespace FOVFix
{
    /// <summary>
    /// Dependency-free compatibility stub. Values remain at defaults when Realism is unavailable.
    /// </summary>
    public class RealismCompat
    {
        public bool HasShoulderContact { get; private set; } = false;
        public bool IsMachinePistol { get; private set; } = false;
        public bool DoAltPistol { get; private set; } = false;
        public float StanceBlenderValue { get; private set; } = 0f;
        public float StanceBlenderTarget { get; private set; } = 0f;
        public bool StancesAreEnabled { get; private set; } = false;
        public bool DoPatrolStanceAdsSmoothing { get; private set; } = false;
        public bool StopCameraMovmentForCollision { get; private set; } = false;
        public float CameraMovmentForCollisionSpeed { get; private set; } = 1f;
        public bool IsColliding { get; private set; } = false;
        public bool IsLeftShoulder { get; private set; } = false;
        public bool IsResettingShoulder { get; private set; } = false;
        public bool IsFiringMovement { get; private set; } = false;
        public bool DoAltRifle { get; private set; } = false;

        public void Update()
        {
            // Intentionally no-op in this bundled reference copy.
        }
    }
}
