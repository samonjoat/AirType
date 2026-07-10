using System;
using System.Windows;
using System.Windows.Controls;
using AirType.ViewModels;
using AirType.Services;

namespace AirType.Views;

public partial class SettingsView : UserControl, IDisposable
{
    private SettingsViewModel? _viewModel;
    private bool _disposed;

    public SettingsView()
    {
        InitializeComponent();
        
        if (Application.Current is App app && app.Services != null)
        {
            _viewModel = new SettingsViewModel(
                app.Services.CredentialManager, 
                app.Services.AppSettingsManager,
                app.Services.AudioInputManager,
                app.Services.HotkeyManager,
                app.Services.GeminiApiClient,
                app.Services.OpenRouterClient,
                app.Services.GroqApiClient,
                app.Services.OfflineEngineManager,
                app.Services.PromptDatabase,
                app.Services.LocalAsrWorkerSupervisor);
            
            _viewModel.SettingsLoaded += ViewModel_SettingsLoaded;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            DataContext = _viewModel;
            
            // Initial load
            UpdatePasswordBoxes(_viewModel);
        }
    }

    private void ViewModel_SettingsLoaded(object? sender, System.EventArgs e)
    {
        if (sender is SettingsViewModel vm)
        {
            UpdatePasswordBoxes(vm);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is SettingsViewModel vm)
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsHotkeyEditMode) && vm.IsHotkeyEditMode)
            {
                Dispatcher.BeginInvoke(new System.Action(() => 
                {
                    HotkeyCaptureTextBox.Focus();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }

            _isUpdatingFromViewModel = true;
            if (e.PropertyName == nameof(SettingsViewModel.GeminiApiKey) && string.IsNullOrEmpty(vm.GeminiApiKey))
            {
                GeminiApiKeyPasswordBox.Password = string.Empty;
            }
            else if (e.PropertyName == nameof(SettingsViewModel.OpenRouterApiKey) && string.IsNullOrEmpty(vm.OpenRouterApiKey))
            {
                OpenRouterApiKeyPasswordBox.Password = string.Empty;
            }
            else if (e.PropertyName == nameof(SettingsViewModel.GroqApiKey) && string.IsNullOrEmpty(vm.GroqApiKey))
            {
                GroqApiKeyPasswordBox.Password = string.Empty;
            }
            _isUpdatingFromViewModel = false;
        }
    }

    private void UpdatePasswordBoxes(SettingsViewModel vm)
    {
        // Suppress events while updating from VM
        _isUpdatingFromViewModel = true;
        GeminiApiKeyPasswordBox.Password = vm.GeminiApiKey;
        OpenRouterApiKeyPasswordBox.Password = vm.OpenRouterApiKey;
        GroqApiKeyPasswordBox.Password = vm.GroqApiKey;
        _isUpdatingFromViewModel = false;
    }

    private bool _isUpdatingFromViewModel;

    private void GeminiApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingFromViewModel && DataContext is SettingsViewModel vm)
        {
            vm.GeminiApiKey = GeminiApiKeyPasswordBox.Password;
        }
    }

    private void OpenRouterApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingFromViewModel && DataContext is SettingsViewModel vm)
        {
            vm.OpenRouterApiKey = OpenRouterApiKeyPasswordBox.Password;
        }
    }

    private void GroqApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingFromViewModel && DataContext is SettingsViewModel vm)
        {
            vm.GroqApiKey = GroqApiKeyPasswordBox.Password;
        }
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        // After command executes, return focus to the capture box
        Dispatcher.BeginInvoke(new System.Action(() => 
        {
            HotkeyCaptureTextBox.Focus();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void RefreshDiagnosticsNow()
    {
        _viewModel?.RefreshDiagnosticsNow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_viewModel != null)
        {
            _viewModel.SettingsLoaded -= ViewModel_SettingsLoaded;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.Dispose();
            _viewModel = null;
        }

        DataContext = null;
    }
}
