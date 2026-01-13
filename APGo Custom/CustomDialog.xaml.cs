namespace APGo_Custom;

public partial class CustomDialog : ContentPage
{
    private TaskCompletionSource<string> _taskCompletionSource;

    public CustomDialog(string title, string message, params string[] buttons)
    {
        InitializeComponent();

        TitleLabel.Text = title;
        MessageLabel.Text = message;

        foreach (var buttonText in buttons)
        {
            var button = new Button
            {
                Text = buttonText,
                BackgroundColor = Colors.Gray,
                TextColor = Colors.White
            };
            button.Clicked += (s, e) => OnButtonClicked(buttonText);
            ButtonContainer.Add(button);
        }
    }

    private async void OnButtonClicked(string result)
    {
        _taskCompletionSource?.TrySetResult(result);
        await Navigation.PopModalAsync();
    }

    public Task<string> ShowAsync()
    {
        _taskCompletionSource = new TaskCompletionSource<string>();
        return _taskCompletionSource.Task;
    }
}