using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using markapp.Helpers;
using markapp.Models;

namespace markapp.Views.Pages
{
    public partial class DocumentsPage : Page
    {
        private Dictionary<string, string> _documentTypes;
        private Dictionary<string, string> _documentStatuses;

        public DocumentsPage()
        {
            InitializeComponent();
            Loaded += DocumentsPage_Loaded;
        }

        private async void DocumentsPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadDictionaries();

                var documents = await FetchDocumentsAsync();
                PopulateDocumentsGroupedByMonth(documents);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке документов: {ex.Message}");
            }
        }

        private void LoadDictionaries()
        {
            _documentTypes = DictionaryHelper.Load("Resources/document_types.json");
            _documentStatuses = DictionaryHelper.Load("Resources/document_statuses.json");
        }

        private async Task<List<DocumentDto>> FetchDocumentsAsync()
        {
            var token = AppState.Instance.Token;
            var groupCode = AppState.Instance.SelectedProductGroupCode;

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var url = $"https://markirovka.crpt.ru/api/v4/true-api/doc/list?limit=10000&pg={groupCode}";
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<DocumentResponse>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            foreach (var doc in data?.Results ?? [])
            {
                if (_documentTypes.TryGetValue(doc.Type, out var typeName))
                    doc.Type = typeName;

                if (_documentStatuses.TryGetValue(doc.Status, out var statusName))
                    doc.Status = statusName;

                if (DateTime.TryParse(doc.ReceivedAt, out var parsed))
                    doc.ReceivedAt = parsed.ToString("dd.MM.yyyy HH:mm");
            }

            return data?.Results ?? new();
        }

        private void PopulateDocumentsGroupedByMonth(List<DocumentDto> documents)
        {
            MonthsPanel.Children.Clear();

            var grouped = documents
                .Where(d => DateTime.TryParse(d.ReceivedAt, out _))
                .GroupBy(d =>
                {
                    var dt = DateTime.ParseExact(d.ReceivedAt, "dd.MM.yyyy HH:mm", new CultureInfo("ru-RU"));
                    return dt.ToString("MMMM yyyy", new CultureInfo("ru-RU"));
                })
                .OrderByDescending(g =>
                    DateTime.ParseExact(g.Key, "MMMM yyyy", new CultureInfo("ru-RU"))
                );

            foreach (var group in grouped)
            {
                var expander = new Expander
                {
                    Header = group.Key,
                    Margin = new Thickness(0, 5, 0, 5),
                    IsExpanded = true,
                    Content = CreateDocumentList(group.ToList())
                };

                MonthsPanel.Children.Add(expander);
            }
        }

        private UIElement CreateDocumentList(List<DocumentDto> docs)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Height = 200,
                Margin = new Thickness(0, 5, 0, 5),
                ItemsSource = docs
            };

            grid.Columns.Add(new DataGridTextColumn { Header = "Получен", Binding = new System.Windows.Data.Binding("ReceivedAt") });
            grid.Columns.Add(new DataGridTextColumn { Header = "Документ", Binding = new System.Windows.Data.Binding("Type") });
            grid.Columns.Add(new DataGridTextColumn { Header = "Номер", Binding = new System.Windows.Data.Binding("Number") });
            grid.Columns.Add(new DataGridTextColumn { Header = "Отправитель", Binding = new System.Windows.Data.Binding("SenderName") });
            grid.Columns.Add(new DataGridTextColumn { Header = "Получатель", Binding = new System.Windows.Data.Binding("ReceiverName") });
            grid.Columns.Add(new DataGridTextColumn { Header = "Статус", Binding = new System.Windows.Data.Binding("Status") });

            return grid;
        }
    }

    public class DocumentResponse
    {
        public List<DocumentDto> Results { get; set; } = new();
        public bool NextPage { get; set; }
    }

    public class DocumentDto
    {
        public string Number { get; set; }
        public string DocDate { get; set; }
        public string ReceivedAt { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string SenderInn { get; set; }
        public string SenderName { get; set; }
        public string ReceiverInn { get; set; }
        public string ReceiverName { get; set; }
        public string DownloadDesc { get; set; }
        public string ProductGroup { get; set; }
    }
}
