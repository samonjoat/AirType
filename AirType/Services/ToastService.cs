using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;

namespace AirType.Services
{
    public enum ToastType { Info, Success, Warning, Error }

    public class ToastNotification
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public ToastType Type { get; set; } = ToastType.Info;
        public PackIconKind IconKind { get; set; } = PackIconKind.InformationOutline;
    }

    public class ToastService
    {
        private const int MaxVisibleToasts = 4;

        private static readonly Lazy<ToastService> _instance = new Lazy<ToastService>(() => new ToastService());
        public static ToastService Instance => _instance.Value;

        public ObservableCollection<ToastNotification> Toasts { get; } = new ObservableCollection<ToastNotification>();
        public ICommand DismissCommand { get; }

        private ToastService()
        {
            DismissCommand = new ToastDismissCommand(this);
        }

        public void Show(string message, ToastType type = ToastType.Info, string title = "")
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => Show(message, type, title)));
                return;
            }

            var duplicate = FindDuplicateToast(title, message, type);
            if (duplicate != null)
            {
                RemoveToast(duplicate);
            }

            while (Toasts.Count >= MaxVisibleToasts)
            {
                RemoveToast(Toasts[0]);
            }

            var toast = new ToastNotification
            {
                Title = title,
                Message = message,
                Type = type,
                IconKind = GetIconKind(type)
            };

            Toasts.Add(toast);

            var timer = new DispatcherTimer { Interval = GetDuration(type) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                RemoveToast(toast);
            };
            timer.Start();
        }

        public void Dismiss(ToastNotification? toast)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => Dismiss(toast)));
                return;
            }

            if (toast != null)
            {
                RemoveToast(toast);
            }
        }

        public void Success(string message, string title = "Success") => Show(message, ToastType.Success, title);
        public void Warning(string message, string title = "Warning") => Show(message, ToastType.Warning, title);
        public void Error(string message, string title = "Error") => Show(message, ToastType.Error, title);
        public void Info(string message, string title = "Info") => Show(message, ToastType.Info, title);

        private ToastNotification? FindDuplicateToast(string title, string message, ToastType type)
        {
            return Toasts.FirstOrDefault(toast =>
                toast.Type == type &&
                string.Equals(toast.Title, title, StringComparison.Ordinal) &&
                string.Equals(toast.Message, message, StringComparison.Ordinal));
        }

        private void RemoveToast(ToastNotification toast)
        {
            Toasts.Remove(toast);
        }

        private static TimeSpan GetDuration(ToastType type)
        {
            return type switch
            {
                ToastType.Warning => TimeSpan.FromSeconds(6),
                ToastType.Error => TimeSpan.FromSeconds(7),
                _ => TimeSpan.FromSeconds(4)
            };
        }

        private static PackIconKind GetIconKind(ToastType type)
        {
            return type switch
            {
                ToastType.Success => PackIconKind.CheckCircleOutline,
                ToastType.Warning => PackIconKind.AlertOutline,
                ToastType.Error => PackIconKind.AlertCircleOutline,
                _ => PackIconKind.InformationOutline
            };
        }

        private sealed class ToastDismissCommand : ICommand
        {
            private readonly ToastService _service;

            public ToastDismissCommand(ToastService service)
            {
                _service = service;
            }

            public event EventHandler? CanExecuteChanged { add { } remove { } }

            public bool CanExecute(object? parameter) => parameter is ToastNotification;

            public void Execute(object? parameter)
            {
                _service.Dismiss(parameter as ToastNotification);
            }
        }
    }
}
