using CenteralES.Admin;
using CenteralES.Infrastructure.AccessControl;
using CenteralES.Infrastructure.Postgres;
using Npgsql;

namespace CenteralES.Admin.Bootstrap.WinForms;

public sealed class MainForm : Form
{
    private readonly TextBox _connectionStringTextBox = new();
    private readonly TextBox _loginTextBox = new();
    private readonly TextBox _passwordTextBox = new();
    private readonly TextBox _commentTextBox = new();
    private readonly TextBox _bootstrapStatusTextBox = new();
    private readonly Button _checkButton = new();
    private readonly Button _bootstrapButton = new();
    private readonly Button _clearConnectionButton = new();

    private readonly TextBox _baseUrlTextBox = new();
    private readonly TextBox _adminLoginTextBox = new();
    private readonly TextBox _adminPasswordTextBox = new();
    private readonly ComboBox _servicesComboBox = new();
    private readonly TextBox _serviceStatusTextBox = new();
    private readonly Button _loginHttpButton = new();
    private readonly Button _loadServicesButton = new();
    private readonly Button _testSelectedServiceButton = new();
    private readonly Button _testAllServicesButton = new();

    private readonly TextBox _demoBaseUrlTextBox = new();
    private readonly TextBox _demoApiKeyTextBox = new();
    private readonly TextBox _demoPdfPathTextBox = new();
    private readonly ComboBox _demoHashAlgorithmComboBox = new();
    private readonly TextBox _demoStatusTextBox = new();
    private readonly Button _runDemoButton = new();
    private readonly Button _selectDemoPdfButton = new();

    private IReadOnlyList<MvpServiceDescriptor> _services = [];
    private MvpHttpTestClient? _mvpClient;
    private Uri? _mvpClientBaseUri;
    private bool _mvpClientLoggedIn;

    public MainForm()
    {
        Text = "CenteralES MVP Test Client";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 760);
        Size = new Size(1080, 820);

        BuildLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mvpClient?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };

        tabs.TabPages.Add(CreateBootstrapTab());
        tabs.TabPages.Add(CreateServicesTab());
        tabs.TabPages.Add(CreatePdfDemoTab());
        Controls.Add(tabs);
    }

    private TabPage CreateBootstrapTab()
    {
        var tab = new TabPage("Первый admin");
        var root = CreateRootLayout(rowCount: 8);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Первичный администратор CenteralES"
        });

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            Margin = new Padding(0, 8, 0, 16),
            Text = "Тестовое WinForms-приложение применяет SQL-миграции и создает первого admin user только если активных администраторов еще нет. Пароль не выводится в статус и audit."
        });

        _connectionStringTextBox.Multiline = true;
        _connectionStringTextBox.Height = 80;
        _connectionStringTextBox.ScrollBars = ScrollBars.Vertical;
        _connectionStringTextBox.PlaceholderText = "Оставьте пустым, чтобы использовать db.env через существующий resolver.";
        root.Controls.Add(CreateLabeledControl("Connection string", _connectionStringTextBox));

        _loginTextBox.PlaceholderText = "admin";
        root.Controls.Add(CreateLabeledControl("Логин", _loginTextBox));

        _passwordTextBox.UseSystemPasswordChar = true;
        _passwordTextBox.PlaceholderText = "Минимум 8 символов";
        root.Controls.Add(CreateLabeledControl("Пароль", _passwordTextBox));

        _commentTextBox.PlaceholderText = "Комментарий для audit, необязательно";
        root.Controls.Add(CreateLabeledControl("Комментарий", _commentTextBox));

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 8, 0, 12)
        };

        _checkButton.Text = "Проверить подключение";
        _checkButton.AutoSize = true;
        _checkButton.Click += async (_, _) => await RunOperationAsync(CheckConnectionAsync, WriteBootstrapStatus);

        _bootstrapButton.Text = "Создать первого администратора";
        _bootstrapButton.AutoSize = true;
        _bootstrapButton.Click += async (_, _) => await RunOperationAsync(BootstrapAdminAsync, WriteBootstrapStatus);

        _clearConnectionButton.Text = "Очистить connection string";
        _clearConnectionButton.AutoSize = true;
        _clearConnectionButton.Click += (_, _) => _connectionStringTextBox.Clear();

        buttons.Controls.Add(_checkButton);
        buttons.Controls.Add(_bootstrapButton);
        buttons.Controls.Add(_clearConnectionButton);
        root.Controls.Add(buttons);

        ConfigureStatusTextBox(
            _bootstrapStatusTextBox,
            "Готово. Connection string можно оставить пустым, если рядом в дереве проекта есть db.env.");
        root.Controls.Add(_bootstrapStatusTextBox);

        tab.Controls.Add(root);
        return tab;
    }

    private TabPage CreateServicesTab()
    {
        var tab = new TabPage("Доступность сервисов");
        var root = CreateRootLayout(rowCount: 7);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Доступность зарегистрированных MVP-сервисов"
        });

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            Margin = new Padding(0, 8, 0, 16),
            Text = "Вкладка проверяет read-only Admin Services registry, /health/live, /health/ready и passive processor status. Для просмотра нужен admin-доступ."
        });

        _baseUrlTextBox.Text = "http://localhost:5045";
        root.Controls.Add(CreateLabeledControl("Web base URL", _baseUrlTextBox));

        var adminCredentials = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 10)
        };
        adminCredentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        adminCredentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _adminLoginTextBox.PlaceholderText = "admin login";
        _adminPasswordTextBox.UseSystemPasswordChar = true;
        _adminPasswordTextBox.PlaceholderText = "admin password";
        adminCredentials.Controls.Add(CreateLabeledControl("Admin login", _adminLoginTextBox), 0, 0);
        adminCredentials.Controls.Add(CreateLabeledControl("Admin password", _adminPasswordTextBox), 1, 0);
        root.Controls.Add(adminCredentials);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 8, 0, 12)
        };

        _loginHttpButton.Text = "Войти в Admin API";
        _loginHttpButton.AutoSize = true;
        _loginHttpButton.Click += async (_, _) => await RunOperationAsync(LoginHttpAsync, WriteServiceStatus);

        _loadServicesButton.Text = "Получить сервисы";
        _loadServicesButton.AutoSize = true;
        _loadServicesButton.Click += async (_, _) => await RunOperationAsync(LoadServicesAsync, WriteServiceStatus);

        _testSelectedServiceButton.Text = "Проверить выбранный";
        _testSelectedServiceButton.AutoSize = true;
        _testSelectedServiceButton.Click += async (_, _) => await RunOperationAsync(TestSelectedServiceAsync, WriteServiceStatus);

        _testAllServicesButton.Text = "Проверить все";
        _testAllServicesButton.AutoSize = true;
        _testAllServicesButton.Click += async (_, _) => await RunOperationAsync(TestAllServicesAsync, WriteServiceStatus);

        buttons.Controls.Add(_loginHttpButton);
        buttons.Controls.Add(_loadServicesButton);
        buttons.Controls.Add(_testSelectedServiceButton);
        buttons.Controls.Add(_testAllServicesButton);
        root.Controls.Add(buttons);

        _servicesComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _servicesComboBox.Dock = DockStyle.Top;
        root.Controls.Add(CreateLabeledControl("Сервисы", _servicesComboBox));

        ConfigureStatusTextBox(
            _serviceStatusTextBox,
            "Готово. Войдите в Admin API, получите список сервисов и проверьте доступность.");
        root.Controls.Add(_serviceStatusTextBox);

        tab.Controls.Add(root);
        return tab;
    }

    private TabPage CreatePdfDemoTab()
    {
        var tab = new TabPage("PDF demo");
        var root = CreateRootLayout(rowCount: 8);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Демонстрация Public API pdf-stamp-recognition"
        });

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            Margin = new Padding(0, 8, 0, 16),
            Text = "Полный сценарий Public API без admin-пароля: загрузка PDF в /api/pdf-stamp-recognition/jobs, чтение /api/jobs/{jobId}, polling результата по hash."
        });

        _demoBaseUrlTextBox.Text = "http://localhost:5045";
        root.Controls.Add(CreateLabeledControl("Web base URL", _demoBaseUrlTextBox));

        _demoApiKeyTextBox.UseSystemPasswordChar = true;
        _demoApiKeyTextBox.PlaceholderText = "Вставьте готовый ключ из админки: keyId.secret";
        root.Controls.Add(CreateLabeledControl("Public API key", _demoApiKeyTextBox));

        _demoHashAlgorithmComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _demoHashAlgorithmComboBox.Items.AddRange(new object[] { "sha256", "gost-r-34.11-2012-256" });
        _demoHashAlgorithmComboBox.SelectedIndex = 0;
        root.Controls.Add(CreateLabeledControl("Hash algorithm", _demoHashAlgorithmComboBox));

        root.Controls.Add(CreateDemoPdfPicker());

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 8, 0, 12)
        };

        _runDemoButton.Text = "Запустить PDF demo";
        _runDemoButton.AutoSize = true;
        _runDemoButton.Click += async (_, _) => await RunOperationAsync(RunPdfDemoAsync, WriteDemoStatus);
        buttons.Controls.Add(_runDemoButton);
        root.Controls.Add(buttons);

        ConfigureStatusTextBox(
            _demoStatusTextBox,
            "Готово. Вставьте Public API key, выберите PDF и запустите demo.");
        root.Controls.Add(_demoStatusTextBox);

        tab.Controls.Add(root);
        return tab;
    }

    private static TableLayoutPanel CreateRootLayout(int rowCount)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = rowCount
        };

        for (var index = 0; index < rowCount - 1; index++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return root;
    }

    private Control CreateDemoPdfPicker()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 10)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _demoPdfPathTextBox.PlaceholderText = "PDF-файл для Public API demo";
        panel.Controls.Add(CreateLabeledControl("PDF file", _demoPdfPathTextBox), 0, 0);

        _selectDemoPdfButton.Text = "Выбрать PDF";
        _selectDemoPdfButton.AutoSize = true;
        _selectDemoPdfButton.Margin = new Padding(8, 20, 0, 0);
        _selectDemoPdfButton.Click += (_, _) => SelectPdfFile();
        panel.Controls.Add(_selectDemoPdfButton, 1, 0);

        return panel;
    }

    private static Control CreateLabeledControl(string labelText, Control control)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 10)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = labelText
        });

        control.Dock = DockStyle.Top;
        panel.Controls.Add(control);

        return panel;
    }

    private static void ConfigureStatusTextBox(TextBox statusTextBox, string initialText)
    {
        statusTextBox.Multiline = true;
        statusTextBox.ReadOnly = true;
        statusTextBox.ScrollBars = ScrollBars.Vertical;
        statusTextBox.Dock = DockStyle.Fill;
        statusTextBox.Text = initialText;
    }

    private async Task RunOperationAsync(
        Func<CancellationToken, Task> operation,
        Action<string> writeStatus)
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        SetBusy(true);
        try
        {
            await operation(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            writeStatus("Операция прервана по timeout.");
        }
        catch (Exception ex)
        {
            writeStatus($"Ошибка: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CheckConnectionAsync(CancellationToken cancellationToken)
    {
        await using var dataSource = await CreateMigratedDataSourceAsync(cancellationToken);
        var bootstrapper = new PostgresAdminBootstrapper(dataSource);
        var activeAdmins = await bootstrapper.CountActiveAdminsAsync(cancellationToken);

        WriteBootstrapStatus($"Подключение и схема проверены. Активных администраторов: {activeAdmins}.");
    }

    private async Task BootstrapAdminAsync(CancellationToken cancellationToken)
    {
        await using var dataSource = await CreateMigratedDataSourceAsync(cancellationToken);
        var bootstrapper = new PostgresAdminBootstrapper(dataSource);

        var result = await bootstrapper.BootstrapFirstAdminAsync(
            new AdminBootstrapUserCommand(
                _loginTextBox.Text,
                _passwordTextBox.Text,
                DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(_commentTextBox.Text) ? null : _commentTextBox.Text,
                "winforms_test_app"),
            cancellationToken);

        switch (result)
        {
            case AdminBootstrapUserSuccess success:
                WriteBootstrapStatus($"Первый администратор создан. Login: {success.User.Login}. Audit id: {success.AuditId:N}.");
                break;
            case AdminBootstrapAlreadyInitialized initialized:
                WriteBootstrapStatus($"Bootstrap не выполнен: уже есть активные администраторы ({initialized.ActiveAdminCount}).");
                break;
            case AdminBootstrapLoginConflict conflict:
                WriteBootstrapStatus($"Bootstrap не выполнен: login '{conflict.Login}' уже существует.");
                break;
            case AdminBootstrapInvalidInput invalidInput:
                WriteBootstrapStatus($"Bootstrap не выполнен: {invalidInput.Message}");
                break;
            default:
                throw new InvalidOperationException($"Unknown bootstrap result '{result.GetType().Name}'.");
        }
    }

    private async Task LoginHttpAsync(CancellationToken cancellationToken)
    {
        var client = CreateOrReuseMvpClient();
        var login = await client.LoginAsync(
            _adminLoginTextBox.Text.Trim(),
            _adminPasswordTextBox.Text,
            cancellationToken);
        _mvpClientLoggedIn = true;
        WriteServiceStatus($"Admin API login выполнен. Пользователь: {login}.");
    }

    private async Task LoadServicesAsync(CancellationToken cancellationToken)
    {
        var client = await EnsureLoggedInMvpClientAsync(cancellationToken);
        _services = await client.DiscoverServicesAsync(cancellationToken);

        _servicesComboBox.Items.Clear();
        foreach (var service in _services)
        {
            _servicesComboBox.Items.Add(service);
        }

        if (_servicesComboBox.Items.Count > 0)
        {
            _servicesComboBox.SelectedIndex = 0;
        }

        var lines = _services.Count == 0
            ? ["Сервисы не найдены."]
            : _services.Select(service =>
                $"Найден сервис: capability={service.Capability}, processor={service.ProcessorKey}, recognizer={service.Recognizer}, endpoints={service.EndpointCount}, contract={service.ContractVersion}");

        WriteServiceStatus(string.Join(Environment.NewLine, lines));
    }

    private async Task TestSelectedServiceAsync(CancellationToken cancellationToken)
    {
        var service = await GetSelectedOrFirstServiceAsync(cancellationToken);
        var results = await _mvpClient!.TestServiceAvailabilityAsync(
            service,
            cancellationToken);

        WriteServiceStatus(FormatTestResults(service, results));
    }

    private async Task TestAllServicesAsync(CancellationToken cancellationToken)
    {
        if (_services.Count == 0)
        {
            await LoadServicesAsync(cancellationToken);
        }

        if (_services.Count == 0)
        {
            WriteServiceStatus("Нет сервисов для тестирования.");
            return;
        }

        var blocks = new List<string>();
        foreach (var service in _services)
        {
            var results = await _mvpClient!.TestServiceAvailabilityAsync(
                service,
                cancellationToken);
            blocks.Add(FormatTestResults(service, results));
        }

        WriteServiceStatus(string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks));
    }

    private async Task RunPdfDemoAsync(CancellationToken cancellationToken)
    {
        using var client = new MvpHttpTestClient(ResolveDemoBaseUri());
        var results = await client.RunPdfStampRecognitionDemoAsync(
            _demoApiKeyTextBox.Text,
            _demoPdfPathTextBox.Text,
            _demoHashAlgorithmComboBox.SelectedItem?.ToString() ?? "sha256",
            cancellationToken);

        var lines = new List<string>
        {
            "Сервис: pdf-stamp-recognition / pdf2txt-http-recognizer",
            $"Hash algorithm: {_demoHashAlgorithmComboBox.SelectedItem ?? "sha256"}"
        };
        lines.AddRange(results.Select(result => result.ToString()));
        WriteDemoStatus(string.Join(Environment.NewLine, lines));
    }

    private async Task<MvpServiceDescriptor> GetSelectedOrFirstServiceAsync(CancellationToken cancellationToken)
    {
        if (_services.Count == 0)
        {
            await LoadServicesAsync(cancellationToken);
        }

        return _servicesComboBox.SelectedItem as MvpServiceDescriptor
            ?? _services.FirstOrDefault()
            ?? throw new InvalidOperationException("Сначала получите список сервисов.");
    }

    private async Task<MvpHttpTestClient> EnsureLoggedInMvpClientAsync(CancellationToken cancellationToken)
    {
        var client = CreateOrReuseMvpClient();
        if (_mvpClientLoggedIn)
        {
            return client;
        }

        await LoginHttpAsync(cancellationToken);
        return _mvpClient!;
    }

    private MvpHttpTestClient CreateOrReuseMvpClient()
    {
        var baseUri = ResolveBaseUri();
        if (_mvpClient is not null && _mvpClientBaseUri == baseUri)
        {
            return _mvpClient;
        }

        _mvpClient?.Dispose();
        _mvpClient = new MvpHttpTestClient(baseUri);
        _mvpClientBaseUri = baseUri;
        _mvpClientLoggedIn = false;
        _services = [];
        _servicesComboBox.Items.Clear();
        return _mvpClient;
    }

    private Uri ResolveBaseUri()
    {
        var configured = _baseUrlTextBox.Text.Trim();
        return ResolveHttpUri(configured, "Web base URL");
    }

    private Uri ResolveDemoBaseUri()
    {
        var configured = _demoBaseUrlTextBox.Text.Trim();
        return ResolveHttpUri(configured, "Demo Web base URL");
    }

    private static Uri ResolveHttpUri(string configured, string displayName)
    {
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{displayName} должен быть абсолютным http/https URL.");
        }

        return uri;
    }

    private async Task<NpgsqlDataSource> CreateMigratedDataSourceAsync(CancellationToken cancellationToken)
    {
        var connectionString = ResolveConnectionString();
        var databaseBootstrapper = new PostgresDatabaseBootstrapper();
        await databaseBootstrapper.EnsureDatabaseAsync(connectionString, cancellationToken);

        var dataSource = NpgsqlDataSource.Create(connectionString);
        await databaseBootstrapper.ApplySchemaAsync(dataSource, cancellationToken);
        return dataSource;
    }

    private string ResolveConnectionString()
    {
        var configured = _connectionStringTextBox.Text.Trim();
        return PostgresDatabaseConnectionStringResolver.Resolve(
            string.IsNullOrWhiteSpace(configured) ? null : configured,
            AppContext.BaseDirectory);
    }

    private void SelectPdfFile()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Title = "Выберите PDF для теста Public API"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _demoPdfPathTextBox.Text = dialog.FileName;
        }
    }

    private void SetBusy(bool busy)
    {
        foreach (var button in new[]
        {
            _checkButton,
            _bootstrapButton,
            _clearConnectionButton,
            _loginHttpButton,
            _loadServicesButton,
            _testSelectedServiceButton,
            _testAllServicesButton,
            _runDemoButton,
            _selectDemoPdfButton
        })
        {
            button.Enabled = !busy;
        }

        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private static string FormatTestResults(
        MvpServiceDescriptor service,
        IReadOnlyList<MvpServiceTestResult> results)
    {
        var lines = new List<string>
        {
            $"Сервис: {service.Capability} / {service.ProcessorKey}",
            $"Recognizer: {service.Recognizer}, endpoints={service.EndpointCount}, contract={service.ContractVersion}"
        };
        lines.AddRange(results.Select(result => result.ToString()));
        return string.Join(Environment.NewLine, lines);
    }

    private void WriteBootstrapStatus(string message)
    {
        _bootstrapStatusTextBox.Text = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}";
    }

    private void WriteServiceStatus(string message)
    {
        _serviceStatusTextBox.Text = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{message}";
    }

    private void WriteDemoStatus(string message)
    {
        _demoStatusTextBox.Text = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{message}";
    }
}
