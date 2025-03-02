using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace TXTtoMediaWiki
{
    internal class Program
    {
        private static HttpClient _httpClient;
        private static CookieContainer _cookieContainer = new CookieContainer();
        private string ApiUrl;

        public Program(string apiUrl, string username)
        {
            ApiUrl = apiUrl;

            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                UseCookies = true,
                AllowAutoRedirect = true
            };

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"{username}/https://dev.miraheze.org/wiki/Converting_text_files_to_MediaWiki_articles");
        }

        static async Task Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: TXTtoMediaWiki.exe <username> <password> <apiUrl>");
                return;
            }

            string username = args[0];
            string password = args[1];
            string apiUrl = args[2];

            var program = new Program(apiUrl, username);
            string loginResult = await program.LoginUserAsync(username, password);

            if (loginResult.Contains("successful"))
            {
                await program.ProcessFilesAsync();
            }
        }

        public async Task<string> LoginUserAsync(string username, string password)
        {
            string? loginToken = await GetTokenAsync("login");

            if (string.IsNullOrWhiteSpace(loginToken))
            {
                throw new InvalidOperationException("Failed to retrieve login token.");
            }

            Dictionary<string, string> parameters = new Dictionary<string, string>
            {
                { "action", "login" },
                { "format", "json" },
                { "lgname", username },
                { "lgpassword", password },
                { "lgtoken", loginToken }
            };

            var encodedContent = new FormUrlEncodedContent(parameters);
            HttpResponseMessage response = await _httpClient.PostAsync(ApiUrl, encodedContent);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Failed to log in. Status code: {response.StatusCode}");
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("login", out JsonElement loginElement) &&
                loginElement.TryGetProperty("result", out JsonElement resultElement))
            {
                string result = resultElement.GetString() ?? "";

                if (result == "Success")
                {
                    return "Login successful!";
                }
                else
                {
                    return $"Login failed. Reason: {result}";
                }
            }

            throw new InvalidOperationException("Unexpected login response format.");
        }

        public async Task<string?> GetTokenAsync(string type)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>
            {
                { "action", "query" },
                { "meta", "tokens" },
                { "format", "json" },
                { "type", type }
            };

            var queryString = string.Join("&", parameters.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            var requestUrl = $"{ApiUrl}?{queryString}";

            HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Failed to retrieve token. Status code: {response.StatusCode}");
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("query", out JsonElement queryElement) &&
                queryElement.TryGetProperty("tokens", out JsonElement tokensElement))
            {
                string tokenKey = $"{type}token";
                if (tokensElement.TryGetProperty(tokenKey, out JsonElement tokenElement))
                {
                    return tokenElement.GetString();
                }
            }

            return null;
        }

        public async Task ProcessFilesAsync()
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            string[] files = Directory.GetFiles(directory, "*.txt");

            if (files.Length == 0)
            {
                Console.WriteLine("No .txt files found.");
                return;
            }

            // Display file names for confirmation
            Console.WriteLine("The following files will be uploaded:");
            foreach (string file in files)
            {
                Console.WriteLine($"- {Path.GetFileName(file)}");
            }

            Console.Write("Do you want to proceed? (Y/N): ");
            string? input = Console.ReadLine()?.Trim().ToLower();
            if (input != "y")
            {
                Console.WriteLine("Operation cancelled.");
                return;
            }

            string? csrfToken = await GetTokenAsync("csrf");
            if (string.IsNullOrWhiteSpace(csrfToken))
            {
                Console.WriteLine("Failed to retrieve CSRF token.");
                return;
            }

            foreach (string file in files)
            {
                string title = Path.GetFileNameWithoutExtension(file);
                string content = await File.ReadAllTextAsync(file);

                Console.WriteLine($"Uploading article: {title}");
                await EditPageAsync(title, content, csrfToken);
            }
        }

        public async Task EditPageAsync(string title, string content, string csrfToken)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>
            {
                { "action", "edit" },
                { "format", "json" },
                { "title", title },
                { "text", content },
                { "token", csrfToken }
            };

            var encodedContent = new FormUrlEncodedContent(parameters);
            HttpResponseMessage response = await _httpClient.PostAsync(ApiUrl, encodedContent);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to edit {title}. Status code: {response.StatusCode}");
                return;
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Edit response for {title}: {jsonResponse}");
        }
    }
}
