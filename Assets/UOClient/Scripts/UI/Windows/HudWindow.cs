using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UOClient
{
    /// <summary>
    /// A movable HUD window: a frame with a title bar (drag to move, × to close) and a body. Windows live in the
    /// HUD's window layer; the <see cref="HudController"/> keeps the open ones in a stack so Esc closes the top one.
    /// </summary>
    public abstract class HudWindow
    {
        public VisualElement Root { get; }
        protected VisualElement Body { get; }
        protected Label Title { get; }
        protected HudServices Hud { get; }

        public bool IsOpen { get; private set; }
        public event Action<HudWindow> Opened, Closed;

        private Vector2 _dragStart, _rootStart;
        private bool _dragging;
        private bool _placed;

        protected HudWindow(HudServices hud, string title, string extraClass, float width, float height)
        {
            Hud = hud;
            Root = new VisualElement();
            Root.AddToClassList("window");
            if (!string.IsNullOrEmpty(extraClass))
            {
                Root.AddToClassList(extraClass);
            }

            Root.style.width = width;
            Root.style.height = height;

            var header = new VisualElement();
            header.AddToClassList("window-header");
            Title = new Label(title);
            Title.AddToClassList("window-title");
            var close = new Button(Close) { text = "×" };
            close.AddToClassList("window-close");
            header.Add(Title);
            header.Add(close);
            Root.Add(header);

            Body = new VisualElement();
            Body.AddToClassList("window-body");
            Root.Add(Body);

            header.RegisterCallback<PointerDownEvent>(OnHeaderDown);
            header.RegisterCallback<PointerMoveEvent>(OnHeaderMove);
            header.RegisterCallback<PointerUpEvent>(OnHeaderUp);
            Root.RegisterCallback<PointerDownEvent>(_ => Root.BringToFront(), TrickleDown.TrickleDown);
            Root.style.display = DisplayStyle.None;
            hud.WindowLayer.Add(Root);
        }

        /// <summary>Where the window first appears (panel pixels from the top-left).</summary>
        protected virtual Vector2 DefaultPosition => new Vector2(400, 140);

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            if (!_placed)
            {
                _placed = true;
                Root.style.left = DefaultPosition.x;
                Root.style.top = DefaultPosition.y;
            }

            Root.style.display = DisplayStyle.Flex;
            Root.BringToFront();
            bool wasOpen = IsOpen;
            IsOpen = true;
            OnOpen();
            if (!wasOpen)
            {
                Opened?.Invoke(this);
            }
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            Root.style.display = DisplayStyle.None;
            Hud.HideContextMenu();
            Hud.HideTooltip();
            OnClose();
            Closed?.Invoke(this);
        }

        protected virtual void OnOpen() { }

        protected virtual void OnClose() { }

        /// <summary>Called every frame while open.</summary>
        public virtual void Update() { }

        private void OnHeaderDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            _dragging = true;
            _dragStart = evt.position;
            _rootStart = new Vector2(Root.resolvedStyle.left, Root.resolvedStyle.top);
            ((VisualElement)evt.currentTarget).CapturePointer(evt.pointerId);
        }

        private void OnHeaderMove(PointerMoveEvent evt)
        {
            if (!_dragging)
            {
                return;
            }

            Vector2 delta = (Vector2)evt.position - _dragStart;
            Root.style.left = _rootStart.x + delta.x;
            Root.style.top = _rootStart.y + delta.y;
        }

        private void OnHeaderUp(PointerUpEvent evt)
        {
            _dragging = false;
            ((VisualElement)evt.currentTarget).ReleasePointer(evt.pointerId);
        }

        // ---- Small builders for subclasses ----

        protected static Label MakeLabel(string text, string cls)
        {
            var label = new Label(text);
            label.AddToClassList(cls);
            return label;
        }

        protected static Button MakeButton(string text, Action onClick, string cls = "hud-button")
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList(cls);
            return button;
        }
    }
}
