using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-417: several placement tests need <c>Camera.main</c> to resolve to null (or to exactly the
    /// camera the test itself created) so on-screen/off-screen placement checks are deterministic. An
    /// EditMode run still has whatever scene the Editor had open when it launched loaded in memory —
    /// e.g. <c>Backyard_Slice.unity</c>, which carries a real MainCamera-tagged object — so
    /// <c>Camera.main</c> is not reliably null just because a test never created a camera itself.
    /// Disable any such ambient camera for the test's duration rather than assume one is absent.
    /// </summary>
    internal static class CameraTestUtil
    {
        public static Camera[] SuppressAmbientMainCameras()
        {
            var found = new System.Collections.Generic.List<Camera>();
            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (!cam.gameObject.activeInHierarchy || !cam.CompareTag("MainCamera")) continue;
                cam.gameObject.SetActive(false);
                found.Add(cam);
            }
            return found.ToArray();
        }

        public static void RestoreAmbientMainCameras(Camera[] suppressed)
        {
            if (suppressed == null) return;
            foreach (Camera cam in suppressed)
                if (cam != null) cam.gameObject.SetActive(true);
        }
    }
}
