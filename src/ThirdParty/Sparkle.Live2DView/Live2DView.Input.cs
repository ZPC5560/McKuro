using Avalonia;
using Avalonia.Input;

namespace Sparkle.Live2DView;

public sealed partial class Live2DView
{
    private bool _dragging;
    private Point _lastPointer;

    private void HookInputEvents()
    {
        _inputLayer.PointerMoved += OnInputPointerMoved;
        _inputLayer.PointerPressed += OnInputPointerPressed;
        _inputLayer.PointerReleased += OnInputPointerReleased;
        _inputLayer.PointerWheelChanged += OnInputPointerWheelChanged;
        _inputLayer.PointerExited += OnInputPointerExited;
    }

    private void OnInputPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_inputLayer).Properties.IsLeftButtonPressed)
            return;

        Point point = e.GetPosition(_inputLayer);
        IReadOnlyList<Live2DHitArea> hits = HitTestAll(point);
        if (hits.Count > 0)
            HitAreaPressed?.Invoke(this, new Live2DHitEventArgs(point, hits));

        if (!CanDrag)
            return;

        _dragging = true;
        _lastPointer = point;
        e.Pointer.Capture(_inputLayer);
        e.Handled = true;
    }

    private void OnInputPointerMoved(object? sender, PointerEventArgs e)
    {
        Point point = e.GetPosition(_inputLayer);

        if (_dragging)
        {
            if (!e.GetCurrentPoint(_inputLayer).Properties.IsLeftButtonPressed)
            {
                EndDrag(e.Pointer);
                return;
            }

            _surface.PanBy(
                point.X - _lastPointer.X,
                point.Y - _lastPointer.Y,
                _inputLayer.Bounds.Height);

            _lastPointer = point;
            e.Handled = true;
            return;
        }

        if (IsPointerFollowEnabled)
            _surface.LookAt(point.X, point.Y, _inputLayer.Bounds.Width, _inputLayer.Bounds.Height);
    }

    private void OnInputPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging)
            e.Handled = true;

        EndDrag(e.Pointer);
    }

    private void OnInputPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!CanZoom)
            return;

        _surface.ZoomBy(e.Delta.Y);
        e.Handled = true;
    }

    private void OnInputPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_dragging && IsPointerFollowEnabled)
            _surface.LookAtCenter();
    }

    private void EndDrag(IPointer pointer)
    {
        _dragging = false;

        if (pointer.Captured == _inputLayer)
            pointer.Capture(null);
    }
}
