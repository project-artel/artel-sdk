using Artel.Affordances.Scan;
using UnityEngine;

namespace Artel
{
    [AddComponentMenu("")]
    internal sealed class ArtelOverlayPresentation : MonoBehaviour
    {
        private const int OverlayLayer = 31;
        private Camera overlayCamera;
        private RenderTexture texture;
        private ArtelOverlayController inputOwner;

        internal static void Attach(Canvas canvas, ArtelOverlayController inputOwner = null)
        {
            var owner = canvas.transform.parent.gameObject;
            var presentation = owner.GetComponent<ArtelOverlayPresentation>();
            if (presentation == null)
            {
                presentation = owner.AddComponent<ArtelOverlayPresentation>();
            }

            canvas.gameObject.layer = OverlayLayer;
            canvas.worldCamera = presentation.overlayCamera;
            canvas.planeDistance = 1f;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            if (inputOwner != null)
            {
                presentation.inputOwner = inputOwner;
            }
        }

        private void Awake()
        {
            var cameraObject = new GameObject("Artel Overlay Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.position = new Vector3(0f, 0f, -10000f);
            cameraObject.transform.rotation = Quaternion.identity;
            cameraObject.AddComponent<Instrument>();
            overlayCamera = cameraObject.AddComponent<Camera>();
            overlayCamera.clearFlags = CameraClearFlags.SolidColor;
            overlayCamera.backgroundColor = Color.clear;
            overlayCamera.cullingMask = 1 << OverlayLayer;
            overlayCamera.nearClipPlane = 0.99f;
            overlayCamera.farClipPlane = 1.01f;
            overlayCamera.depth = short.MaxValue;
            overlayCamera.allowHDR = false;
            overlayCamera.allowMSAA = false;
            overlayCamera.useOcclusionCulling = false;
            EnsureTexture();
        }

        private void Update()
        {
            EnsureTexture();
        }

        private void OnGUI()
        {
            var current = Event.current;
            if (current.type == EventType.Repaint && texture != null)
            {
                GUI.depth = -100;
                var color = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), texture);
                GUI.color = color;
            }
            else if (inputOwner != null && current.isMouse &&
                     inputOwner.ContainsOverlayInput(current.mousePosition))
            {
                current.Use();
            }
        }

        private void EnsureTexture()
        {
            var width = Mathf.Max(1, Screen.width);
            var height = Mathf.Max(1, Screen.height);
            if (texture != null && texture.width == width && texture.height == height)
            {
                return;
            }

            if (texture != null)
            {
                Destroy(texture);
            }

            texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Artel Overlay"
            };
            overlayCamera.targetTexture = texture;
        }

        private void OnDestroy()
        {
            if (overlayCamera != null)
            {
                Destroy(overlayCamera.gameObject);
            }

            if (texture != null)
            {
                Destroy(texture);
            }
        }
    }
}
