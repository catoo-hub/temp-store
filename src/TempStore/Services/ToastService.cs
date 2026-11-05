namespace TempStore.Services;
public record ToastMessage(Guid Id, string Text, bool IsError);
public sealed class ToastService
{
    public event Action<ToastMessage>? Raised;
    public void Show(string text, bool error = false) => Raised?.Invoke(new(Guid.NewGuid(), text, error));
}
