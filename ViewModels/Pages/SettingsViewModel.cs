using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using markapp.Helpers;
using System.IO;
using markapp.Models;


public partial class SettingsViewModel : ObservableObject
{
    public ObservableCollection<X509Certificate2> Certificates { get; } = new();

    private X509Certificate2? _selectedCertificate;
    public X509Certificate2? SelectedCertificate
    {
        get => _selectedCertificate;
        set => SetProperty(ref _selectedCertificate, value);
    }

    public string LogFilePath => LogHelper.LogFilePath;

    public ICommand SelectLogFilePathCommand { get; }

    public SettingsViewModel()
    {
        SelectLogFilePathCommand = new RelayCommand(() =>
        {
            LogHelper.SelectLogFilePath();
            OnPropertyChanged(nameof(LogFilePath));
        });

        ConnectToGisMtCommand = new RelayCommand(ConnectToGisMt);

        LoadCertificates();
        LoadProductGroups();
    }


    private void LoadCertificates()
    {
        Certificates.Clear();

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        foreach (var cert in store.Certificates)
        {
            if (cert.HasPrivateKey)
                Certificates.Add(cert);
        }
    }

    public ICommand ConnectToGisMtCommand { get; }


    public string? AuthToken
    {
        get => AppState.Instance.Token;
        private set => AppState.Instance.Token = value;
    }

    private async void ConnectToGisMt()
    {
        try
        {
            LogHelper.WriteLog("ConnectToGisMt", "Начало подключения к ГИС МТ");

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // 🔹 Шаг 1: Получение UUID и строки для подписи
            var response = await http.GetAsync("https://markirovka.crpt.ru/api/v3/true-api/auth/key");
            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show("Ошибка получения UUID", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var authKey = JsonSerializer.Deserialize<AuthKeyResponse>(json);

            if (authKey == null || string.IsNullOrWhiteSpace(authKey.uuid) || string.IsNullOrWhiteSpace(authKey.data))
            {
                MessageBox.Show("Пустые UUID или data", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LogHelper.WriteLog("auth/key", $"uuid: {authKey.uuid}\ndata: {authKey.data}");

            // 🔹 Шаг 2: Используем выбранный сертификат или предлагаем выбор
            X509Certificate2 cert;

            if (SelectedCertificate != null)
            {
                cert = SelectedCertificate;
                LogHelper.WriteLog("Сертификат", "Выбран из выпадающего списка");
            }
            else
            {
                var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);

                var selected = X509Certificate2UI.SelectFromCollection(
                    store.Certificates,
                    "Выбор сертификата",
                    "Выберите сертификат для подписи",
                    X509SelectionFlag.SingleSelection
                );

                if (selected.Count == 0)
                {
                    MessageBox.Show("Выбор отменён", "Отмена", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                cert = selected[0];
                SelectedCertificate = cert;
                LogHelper.WriteLog("Сертификат", "Выбран через диалоговое окно");
            }

            // Общие действия после выбора
            AppState.Instance.SelectedCertificate = cert;
            AppState.Instance.CertificateOwner = cert.Subject;

            string? fio = cert.GetNameInfo(X509NameType.SimpleName, false); // ← только имя
            AppState.Instance.CertificateOwnerPublicName = fio?.ToUpperInvariant(); // стандарт как на скрине
            var dn = cert.GetNameInfo(X509NameType.SimpleName, false);
            LogHelper.WriteLog("Выбран сертификат", dn);


            // 🔹 Шаг 3: Подготовка путей в корне приложения
            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            string exePath;

            try
            {
                exePath = EmbeddedExtractor.EnsureCryptcpExtracted();
                LogHelper.WriteLog("cryptcp", $"Извлечён cryptcp.exe: {exePath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при извлечении cryptcp.exe: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string dataPath = Path.Combine(appDir, "data.txt");
            string signPath = Path.Combine(appDir, "data_sign.txt");

            // Проверка cryptcp и загрузка
            if (!File.Exists(exePath))
            {
                try
                {
                    LogHelper.WriteLog("cryptcp", "Файл cryptcp.win32.exe не найден. Начинаю загрузку...");

                    using var wc = new System.Net.WebClient();
                    wc.DownloadFile("https://www.upload.ee/download/18171330/f4204c1202b020c58c4a/cryptcp.win32.exe", exePath);

                    LogHelper.WriteLog("cryptcp", "Загрузка cryptcp.win32.exe завершена.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось загрузить cryptcp.exe: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // Создание файлов при отсутствии
            if (!File.Exists(dataPath)) File.Create(dataPath).Dispose();
            if (!File.Exists(signPath)) File.Create(signPath).Dispose();

            File.WriteAllText(dataPath, authKey.data);

            // 🔹 Шаг 4: Подпись через cryptcp
            string cmdArgs = $"/c chcp 1251 > nul && \"{exePath}\" -sign -dn \"{dn}\" \"{dataPath}\" \"{signPath}\"";

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            LogHelper.WriteLog("cryptcp → CMD", cmdArgs);
            process.Start();
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            LogHelper.WriteLog("cryptcp → STDOUT", stdout);
            LogHelper.WriteLog("cryptcp → STDERR", stderr);

            if (process.ExitCode != 0)
            {
                MessageBox.Show("Ошибка cryptcp", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string signature = File.ReadAllText(signPath).Replace("\r", "").Replace("\n", "");
            LogHelper.WriteLog("Подпись", signature.Length > 100 ? signature[..100] + "..." : signature);

            // 🔹 Шаг 5: simpleSignIn
            var signInRequest = new { uuid = authKey.uuid, data = signature };
            var contentJson = new StringContent(JsonSerializer.Serialize(signInRequest), Encoding.UTF8, "application/json");

            var tokenResponse = await http.PostAsync("https://markirovka.crpt.ru/api/v3/true-api/auth/simpleSignIn", contentJson);
            var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
            LogHelper.WriteLog("simpleSignIn → RAW", tokenJson);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                MessageBox.Show($"Ошибка авторизации: {tokenJson}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var tokenResult = JsonSerializer.Deserialize<TokenResponse>(tokenJson);
            if (tokenResult == null || string.IsNullOrEmpty(tokenResult.token))
            {
                MessageBox.Show("Не удалось получить токен", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            AuthToken = tokenResult.token;
            await LoadUserProfileAndFilterProductGroups();
            AppState.Instance.NotifyTokenUpdated();

            MessageBox.Show("Успешно получен токен!", "ГИС МТ");
        }
        catch (Exception ex)
        {
            LogHelper.WriteLog("ConnectToGisMt → Ошибка", ex.ToString());
            MessageBox.Show($"Ошибка: {ex.Message}", "Исключение", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadUserProfileAndFilterProductGroups()
    {
        if (string.IsNullOrWhiteSpace(AuthToken))
            return;

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

            var response = await http.GetAsync("https://markirovka.crpt.ru/api/v3/facade/profile/company2");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var profile = JsonSerializer.Deserialize<CompanyProfileDto>(json);

            var allowedCodes = profile?.productGroupsAndRoles.Select(p => p.code).ToHashSet() ?? new();

            // обновляем ProductGroups
            foreach (var pg in ProductGroups)
            {
                pg.IsEnabled = allowedCodes.Contains(pg.code);
            }

            // сортируем: сначала доступные, затем заблокированные
            var sorted = ProductGroups.OrderByDescending(p => p.IsEnabled).ThenBy(p => p.name).ToList();
            ProductGroups.Clear();
            foreach (var item in sorted)
                ProductGroups.Add(item);
        }
        catch (Exception ex)
        {
            LogHelper.WriteLog("LoadUserProfile", ex.ToString());
            MessageBox.Show("Ошибка получения профиля участника.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }



    public ObservableCollection<ProductGroupDto> ProductGroups { get; } = new();

    private ProductGroupDto? _selectedProductGroup;
    public ProductGroupDto? SelectedProductGroup
    {
        get => _selectedProductGroup;
        set
        {
            SetProperty(ref _selectedProductGroup, value);
            if (value != null)
            {
                AppState.Instance.SelectedProductGroupCode = value.code;
                AppState.Instance.SelectedProductGroupName = value.name;
            }
        }
    }

    public void LoadProductGroups()
    {
        var json = File.ReadAllText("Resources/product_groups.json"); // или из embedded
        var root = JsonSerializer.Deserialize<ProductGroupRoot>(json);
        ProductGroups.Clear();
        foreach (var pg in root.result)
        {
            ProductGroups.Add(pg);
        }
    }

    private class ProductGroupRoot
    {
        public List<ProductGroupDto> result { get; set; } = new();
    }


    public class CompanyProfileDto
    {
        public List<ProductGroupRole> productGroupsAndRoles { get; set; } = new();

        public class ProductGroupRole
        {
            public string code { get; set; } = string.Empty;
        }
    }

    private record AuthKeyResponse(string uuid, string data);
    private record TokenResponse(string token);
}
