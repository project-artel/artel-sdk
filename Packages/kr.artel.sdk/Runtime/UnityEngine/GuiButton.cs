using global::UnityEngine;

namespace Artel
{
    public static partial class ArtelInput
    {
        private static int guiPressFrame = -1;
        private static int guiPressedControl;
        private static int guiRepaintFrame = -1;
        internal static bool GuiPressHandledByEventSystem { get; set; }
        internal static string LastGuiPressReceiver { get; private set; }
        internal static bool HasRecentGuiButtons => guiRepaintFrame >= Time.frameCount - 2 && guiRepaintFrame >= 0;

        public static bool Button(Rect position, string text) =>
            ObserveGuiButton(position, text, GUI.Button(position, text));

        public static bool Button(Rect position, Texture image) =>
            ObserveGuiButton(position, null, GUI.Button(position, image));

        public static bool Button(Rect position, GUIContent content) =>
            ObserveGuiButton(position, content?.text, GUI.Button(position, content));

        public static bool Button(Rect position, string text, GUIStyle style) =>
            ObserveGuiButton(position, text, GUI.Button(position, text, style));

        public static bool Button(Rect position, Texture image, GUIStyle style) =>
            ObserveGuiButton(position, null, GUI.Button(position, image, style));

        public static bool Button(Rect position, GUIContent content, GUIStyle style) =>
            ObserveGuiButton(position, content?.text, GUI.Button(position, content, style));

        private static bool ObserveGuiButton(Rect position, string label, bool clicked)
        {
            var control = GUIUtility.GetControlID(FocusType.Passive, position);
            if (Event.current.type != EventType.Repaint)
                return clicked;

            var frame = Time.frameCount;
            guiRepaintFrame = frame;
            bool down = VirtualMouse.GetButtonDown(0, frame);
            bool up = VirtualMouse.GetButtonUp(0, frame);
            if (GuiPressHandledByEventSystem || (!down && !up) ||
                !OwnsVirtualPointer)
                return clicked;

            var pointer = VirtualMouse.Position;
            // IMGUI 좌표는 위에서 시작하며 GUI 그룹과 행렬의 영향을 받는다.
            var localPosition = GUIUtility.ScreenToGUIPoint(new Vector2(pointer.x, Screen.height - pointer.y));
            if (down && guiPressFrame != frame && GUI.enabled &&
                position.Contains(localPosition))
            {
                guiPressFrame = frame;
                guiPressedControl = control;
                LastGuiPressReceiver = string.IsNullOrEmpty(label) ? "IMGUI button" : label;
            }

            if (!up || guiPressedControl != control)
                return clicked;

            guiPressedControl = 0;
            if (!GUI.enabled || !position.Contains(localPosition))
                return clicked;

            GUI.changed = true;
            return true;
        }

        private static void CancelGuiPress()
        {
            guiPressedControl = 0;
            guiPressFrame = -1;
            LastGuiPressReceiver = null;
            GuiPressHandledByEventSystem = false;
        }
    }
}
