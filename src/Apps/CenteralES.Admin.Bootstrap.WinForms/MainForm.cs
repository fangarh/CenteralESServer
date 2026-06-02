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
    private readonly TextBox _statusTextBox = new();
    private readonly Button _checkButton = new();
    private readonly Button _bootstrapButton = new();
    private readonly Button _clearConnectionButton = new();

    public MainForm()
    {
        Text = "CenteralES Admin Bootstrap";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 620);
        Size = new Size(900, 680);

        BuildLayout();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 8
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Первичный администратор CenteralES"
        };
        root.Controls.Add(title);

        var description = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            Margin = new Padding(0, 8, 0, 16),
            Text = "Тестовое WinForms-приложение применяет SQL-миграции и создает первого admin user только если активных администраторов еще нет. Пароль не выводится в статус и audit."
        };
        root.Controls.Add(description);

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
        _checkButton.Click += async (_, _) => await RunOperationAsync(CheckConnectionAsync);

        _bootstrapButton.Text = "Создать первого администратора";
        _bootstrapButton.AutoSize = true;
        _bootstrapButton.Click += async (_, _) => await RunOperationAsync(BootstrapAdminAsync);

        _clearConnectionButton.Text = "Очистить connection string";
        _clearConnectionButton.AutoSize = true;
        _clearConnectionButton.Click += (_, _) => _connectionStringTextBox.Clear();

        buttons.Controls.Add(_checkButton);
        buttons.Controls.Add(_bootstrapButton);
        buttons.Controls.Add(_clearConnectionButton);
        root.Controls.Add(buttons);

        _statusTextBox.Multiline = true;
        _statusTextBox.ReadOnly = true;
        _statusTextBox.ScrollBars = ScrollBars.Vertical;
        _statusTextBox.Dock = DockStyle.Fill;
        _statusTextBox.Text = "Готово. Connection string можно оставить пустым, если рядом в дереве проекта есть db.env.";
        root.Controls.Add(_statusTextBox);

        Controls.Add(root);
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

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        SetBusy(true);
        try
        {
            await operation(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            WriteStatus("Операция прервана по timeout.");
        }
        catch (Exception ex)
        {
            WriteStatus($"Ошибка: {ex.Message}");
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

        WriteStatus($"Подключение и схема проверены. Активных администраторов: {activeAdmins}.");
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
                WriteStatus($"Первый администратор создан. Login: {success.User.Login}. Audit id: {success.AuditId:N}.");
                break;
            case AdminBootstrapAlreadyInitialized initialized:
                WriteStatus($"Bootstrap не выполнен: уже есть активные администраторы ({initialized.ActiveAdminCount}).");
                break;
            case AdminBootstrapLoginConflict conflict:
                WriteStatus($"Bootstrap не выполнен: login '{conflict.Login}' уже существует.");
                break;
            case AdminBootstrapInvalidInput invalidInput:
                WriteStatus($"Bootstrap не выполнен: {invalidInput.Message}");
                break;
            default:
                throw new InvalidOperationException($"Unknown bootstrap result '{result.GetType().Name}'.");
        }
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

    private void SetBusy(bool busy)
    {
        _checkButton.Enabled = !busy;
        _bootstrapButton.Enabled = !busy;
        _clearConnectionButton.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void WriteStatus(string message)
    {
        _statusTextBox.Text = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}";
    }
}
