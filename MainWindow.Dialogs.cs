using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SpriteLens;

public partial class MainWindow
{
    private void ShowHelp()
    {
        ShowModal(HelpCard);
    }

    private void CloseHelp_Click(object sender, RoutedEventArgs e) => CloseModal();

    private void ModalLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ModalLayer)) return;

        if (ConfirmCard.Visibility == Visibility.Visible)
        {
            _confirmTcs?.TrySetResult(false);
            CloseModal();
        }
        else if (InputCard.Visibility == Visibility.Visible)
        {
            _inputTcs?.TrySetResult(null);
            CloseModal();
        }
        else
        {
            CloseModal();
        }
    }

    private void ShowModal(Border card)
    {
        HelpCard.Visibility = ReferenceEquals(card, HelpCard) ? Visibility.Visible : Visibility.Collapsed;
        ConfirmCard.Visibility = ReferenceEquals(card, ConfirmCard) ? Visibility.Visible : Visibility.Collapsed;
        InputCard.Visibility = ReferenceEquals(card, InputCard) ? Visibility.Visible : Visibility.Collapsed;

        ModalLayer.Visibility = Visibility.Visible;
        _modalOpen = true;
        card.Opacity = 0;
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(card, ConfirmCard))
            {
                ConfirmOkButton.Focus();
            }
            else if (ReferenceEquals(card, InputCard))
            {
                InputBox.Focus();
                InputBox.SelectAll();
            }
            else
            {
                ModalLayer.Focus();
            }
        }), DispatcherPriority.Input);
    }

    private void CloseModal()
    {
        ModalLayer.Visibility = Visibility.Collapsed;
        HelpCard.Visibility = Visibility.Collapsed;
        ConfirmCard.Visibility = Visibility.Collapsed;
        InputCard.Visibility = Visibility.Collapsed;
        ConfirmOkButton.IsDefault = false;
        _modalOpen = false;
    }

    private async Task<bool> ConfirmAsync(string title, string message, string okText = "确定", bool danger = false)
    {
        if (_confirmTcs != null)
        {
            _confirmTcs.TrySetResult(false);
            _confirmTcs = null;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirmTcs = completion;

        ConfirmTitleText.Text = title;
        ConfirmBodyText.Text = message;
        ConfirmOkButton.Content = okText;
        ConfirmOkButton.Style = (Style)FindResource(danger ? "Button.Danger" : "Button.Primary");
        ConfirmOkButton.IsDefault = true;

        ShowModal(ConfirmCard);
        return await completion.Task;
    }

    private void ConfirmOk_Click(object sender, RoutedEventArgs e)
    {
        var completion = _confirmTcs;
        _confirmTcs = null;
        CloseModal();
        completion?.TrySetResult(true);
    }

    private void ConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        var completion = _confirmTcs;
        _confirmTcs = null;
        CloseModal();
        completion?.TrySetResult(false);
    }

    private async Task<string?> PromptAsync(string title, string hint, string initial)
    {
        if (_inputTcs != null)
        {
            _inputTcs.TrySetResult(null);
            _inputTcs = null;
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inputTcs = completion;

        InputTitleText.Text = title;
        InputHintText.Text = hint;
        InputBox.Text = initial;

        ShowModal(InputCard);
        return await completion.Task;
    }

    private void InputOk_Click(object sender, RoutedEventArgs e)
    {
        var completion = _inputTcs;
        _inputTcs = null;
        string value = InputBox.Text.Trim();
        CloseModal();
        completion?.TrySetResult(value);
    }

    private void InputCancel_Click(object sender, RoutedEventArgs e)
    {
        var completion = _inputTcs;
        _inputTcs = null;
        CloseModal();
        completion?.TrySetResult(null);
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            InputOk_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            InputCancel_Click(sender, e);
            e.Handled = true;
        }
    }

    private void CancelOpenModal()
    {
        if (ConfirmCard.Visibility == Visibility.Visible)
        {
            _confirmTcs?.TrySetResult(false);
            _confirmTcs = null;
        }
        else if (InputCard.Visibility == Visibility.Visible)
        {
            _inputTcs?.TrySetResult(null);
            _inputTcs = null;
        }

        CloseModal();
    }
}
