using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using HermesAgentTray.Services;

namespace HermesAgentTray;

public partial class ProfileDialog : Window
{
    public string ProfileName => TxtProfileName.Text.Trim();
    public Dictionary<string, string> EnvValues { get; private set; } = new();

    private readonly bool _isEdit;

    // Keys that are user-specific and should be configured per profile
    private static readonly string[] DiffKeys = new[]
    {
        // Platform tokens — each profile needs its own, never shared
        "WEIXIN_TOKEN", "WEIXIN_ACCOUNT_ID",
        "QQ_APP_ID", "QQ_CLIENT_SECRET",
        "TELEGRAM_BOT_TOKEN",
        "SLACK_BOT_TOKEN", "SLACK_APP_TOKEN",
        "DISCORD_TOKEN", "DISCORD_CLIENT_ID", "DISCORD_CLIENT_SECRET",
        "TEAMS_APP_ID", "TEAMS_APP_PASSWORD",
        "GOOGLE_CHAT_KEY", "GOOGLE_CHAT_PROJECT_ID",
        // User allowlists — per-profile access control
        "TELEGRAM_ALLOWED_USERS",
        "TELEGRAM_HOME_CHANNEL",
        "DISCORD_ALLOWED_USERS",
        "SLACK_ALLOWED_USERS",
        "WEIXIN_ALLOWED_USERS",
        "WEIXIN_HOME_CHANNEL",
        "WEIXIN_GROUP_ALLOWED_USERS",
        "QQ_ALLOWED_USERS",
        "QQBOT_HOME_CHANNEL",
        "GATEWAY_ALLOW_ALL_USERS",
        "TEAMS_ALLOWED_USERS",
        "TEAMS_HOME_CHANNEL",
        "GOOGLE_CHAT_ALLOWED_USERS",
        "GOOGLE_CHAT_HOME_CHANNEL",
    };

    public ProfileDialog() : this("", new Dictionary<string, string>())
    {
        _isEdit = false;
    }

    public ProfileDialog(string existingName, Dictionary<string, string> existingEnv)
    {
        InitializeComponent();

        _isEdit = !string.IsNullOrEmpty(existingName);
        TxtProfileName.Text = existingName;
        TxtProfileName.IsReadOnly = _isEdit;

        // Apply localization
        LblProfileName.Text = Loc.ProfileNameLabel;
        BtnLoadEnv.Content = Loc.LoadEnv;
        LblEnvVars.Text = "Environment Variables (.env)";
        BtnAddVar.Content = Loc.AddVar;
        BtnOk.Content = "OK";
        BtnCancelDlg.Content = Loc.Cancel;

        if (_isEdit)
        {
            Title = Loc.EditProfileTitle(existingName);
            TxtNameHint.Text = Loc.ProfileNameLocked;
        }
        else
        {
            Title = Loc.NewProfileTitle;
            TxtNameHint.Text = Loc.ProfileNameHint;
        }

        // For new profiles: only show diff keys (inherited from default)
        // For edit: show all existing keys + diff keys as placeholders
        if (!_isEdit)
        {
            var defaultEnv = Services.ProfileManager.LoadEnv("default");
            // Keys that are platform tokens — should NOT be pre-filled from default
            var platformTokenSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "WEIXIN_TOKEN", "WEIXIN_ACCOUNT_ID",
                "QQ_APP_ID", "QQ_CLIENT_SECRET",
                "TELEGRAM_BOT_TOKEN",
                "SLACK_BOT_TOKEN", "SLACK_APP_TOKEN",
                "DISCORD_TOKEN", "DISCORD_CLIENT_ID", "DISCORD_CLIENT_SECRET",
                "TEAMS_APP_ID", "TEAMS_APP_PASSWORD",
                "GOOGLE_CHAT_KEY", "GOOGLE_CHAT_PROJECT_ID",
            };

            foreach (var key in DiffKeys)
            {
                var value = existingEnv.GetValueOrDefault(key, "");
                // Only pre-fill from default for non-token keys (allowlists, channels, etc.)
                // Platform tokens must be unique per profile — never copy from default
                if (string.IsNullOrEmpty(value) && !platformTokenSet.Contains(key) && defaultEnv.TryGetValue(key, out var defaultVal))
                    value = defaultVal;
                AddEnvRow(key, value, isInherited: !string.IsNullOrEmpty(value) && !platformTokenSet.Contains(key));
            }
        }
        else
        {
            // Edit mode: show all keys from existing env
            foreach (var kv in existingEnv)
            {
                AddEnvRow(kv.Key, kv.Value, isInherited: false);
            }
        }

        // Always have at least one empty row for adding custom vars
        AddEnvRow("", "", isInherited: false);
    }

    private void AddEnvRow(string key, string value, bool isInherited = false)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var txtKey = new TextBox
        {
            Text = key,
            Padding = new Thickness(6, 4, 6, 4),
            Tag = "key",
            IsReadOnly = !_isEdit && !string.IsNullOrEmpty(key), // Lock pre-defined keys in new profile mode
            Background = !_isEdit && !string.IsNullOrEmpty(key)
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0xF0, 0xF0))
                : System.Windows.Media.Brushes.White,
        };
        Grid.SetColumn(txtKey, 0);
        row.Children.Add(txtKey);

        var txtValue = new TextBox
        {
            Text = value,
            Padding = new Thickness(6, 4, 6, 4),
            Tag = "value",
        };
        if (isInherited && string.IsNullOrEmpty(value))
        {
            txtValue.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA));
        }
        Grid.SetColumn(txtValue, 2);
        row.Children.Add(txtValue);

        var btnRemove = new Button
        {
            Content = "X",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(4, 0, 0, 0),
            FontSize = 10,
            Visibility = _isEdit || string.IsNullOrEmpty(key) ? Visibility.Visible : Visibility.Collapsed,
        };
        btnRemove.Click += (_, _) => EnvPanel.Children.Remove(row);
        Grid.SetColumn(btnRemove, 3);
        row.Children.Add(btnRemove);

        EnvPanel.Children.Add(row);
    }

    private void BtnAddVar_Click(object sender, RoutedEventArgs e)
    {
        AddEnvRow("", "", isInherited: false);
    }

    private void BtnLoadEnv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Env files (.env)|*.env|All files|*.*",
            Title = "Load .env file",
        };

        if (dlg.ShowDialog() == true)
        {
            foreach (var line in File.ReadAllLines(dlg.FileName))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                {
                    var key = trimmed[..eqIdx].Trim();
                    var val = trimmed[(eqIdx + 1)..].Trim().Trim('"');
                    AddEnvRow(key, val);
                }
            }
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (!_isEdit)
        {
            try
            {
                Services.ProfileManager.ValidateProfileName(ProfileName);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message, "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // Collect env values (only non-empty values)
        EnvValues.Clear();
        foreach (var child in EnvPanel.Children)
        {
            if (child is Grid row)
            {
                var keyBox = row.Children.OfType<TextBox>().FirstOrDefault(t => t.Tag?.ToString() == "key");
                var valBox = row.Children.OfType<TextBox>().FirstOrDefault(t => t.Tag?.ToString() == "value");
                if (keyBox != null && valBox != null && !string.IsNullOrWhiteSpace(keyBox.Text))
                {
                    EnvValues[keyBox.Text.Trim()] = valBox.Text;
                }
            }
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
