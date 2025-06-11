using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using markapp.Models;
using markapp.Helpers;

namespace markapp.ViewModels.Pages
{
    public partial class DataViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<RequestedCisItem> _requestedCisList = new();

        private const string ApiUrl = "https://markirovka.crpt.ru/api/v3/true-api/cises/info";
        private string LogFilePath => LogHelper.LogFilePath;

        [RelayCommand]
        public async Task LoadFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show("Файл не найден.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var lines = await File.ReadAllLinesAsync(filePath);
            var cisList = lines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct()
                .ToList();

            RequestedCisList.Clear();
            foreach (var cis in cisList)
            {
                RequestedCisList.Add(new RequestedCisItem
                {
                    RequestedCis = cis,
                    ProductName = "N/D",
                    Status = "N/D"
                });
            }

            if (RequestedCisList.Count == 0)
            {
                MessageBox.Show("Файл пуст или не содержит корректных кодов!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await FetchCisInfoBatchedAsync();
        }

        public async Task FetchCisInfoBatchedAsync()
        {
            string token = AppState.Instance.Token;
            if (string.IsNullOrWhiteSpace(token))
            {
                MessageBox.Show("Токен не получен. Сначала подключитесь к ГИС МТ.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            int batchSize = 1000;
            int totalBatches = (int)Math.Ceiling((double)RequestedCisList.Count / batchSize);

            var responseList = new List<ApiResponse>();

            for (int i = 0; i < totalBatches; i++)
            {
                var batch = RequestedCisList
                    .Skip(i * batchSize)
                    .Take(batchSize)
                    .Select(item => item.RequestedCis?.Trim() ?? "")
                    .Select(cis => cis.Length >= 25 ? cis.Substring(0, 25) : cis)
                    .Where(cis => !string.IsNullOrEmpty(cis) && cis.Length == 25)
                    .Select(cis => $"\"{cis.Replace("\"", "\\\"")}\"")
                    .ToList();

                if (batch.Count == 0) continue;

                string requestBody = "[\n    " + string.Join(",\n    ", batch) + "\n]";
                try
                {
                    var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(ApiUrl, content);
                    string responseData = await response.Content.ReadAsStringAsync();

                    LogToFile("=== API REQUEST ===");
                    LogToFile(requestBody);
                    LogToFile("=== API RESPONSE ===");
                    LogToFile(responseData);

                    if (response.IsSuccessStatusCode)
                    {
                        var parsed = JsonConvert.DeserializeObject<List<ApiResponse>>(responseData);
                        if (parsed != null)
                            responseList.AddRange(parsed);
                    }
                }
                catch (Exception ex)
                {
                    LogToFile("=== EXCEPTION ===");
                    LogToFile(ex.ToString());
                    MessageBox.Show($"Ошибка запроса: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            UpdateTable(responseList);
        }

        private void UpdateTable(List<ApiResponse> responseList)
        {
            foreach (var response in responseList)
            {
                var item = RequestedCisList.FirstOrDefault(c =>
                    c.RequestedCis?.StartsWith(response.CisInfo.RequestedCis) == true);

                if (item != null)
                {
                    item.ProductName = response.CisInfo.ProductName;
                    item.Status = GetStatusDescription(response.CisInfo.Status);
                    item.OwnerName = response.CisInfo.OwnerName;
                }
            }

            OnPropertyChanged(nameof(RequestedCisList));
        }

        private string GetStatusDescription(string status) => status switch
        {
            "EMITTED" => "Эмитирован",
            "APPLIED" => "Нанесён",
            "INTRODUCED" => "В обороте",
            "WRITTEN_OFF" => "Списан",
            "WITHDRAWN" => "Выбыл",
            _ => "Неизвестно"
        };

        private void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch
            {
                // Игнорируем ошибки логирования
            }
        }

        public bool HasSelectedAppliedItems =>
            RequestedCisList.Any(item => item.IsSelected && item.Status == "Нанесён");

        public class ApiResponse
        {
            public CisInfo CisInfo { get; set; }
        }
    }
}
