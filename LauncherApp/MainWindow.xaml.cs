using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace LauncherApp
{
    public partial class MainWindow : Window
    {
        private const string AppName = "MyAppLauncher";
        private const string GitHubRepo = "Danhik78/PublisTest";

        private readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppName);

        private readonly string AppsDir;
        private readonly string DotnetDir;

        public MainWindow()
        {
            InitializeComponent();
            AppsDir = Path.Combine(AppDataDir, "Apps");
            DotnetDir = Path.Combine(AppDataDir, "dotnet");

            
            // Запускаем инициализацию асинхронно
            Loaded += async (s, e) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                UpdateStatus("Preparing environment...");
                Directory.CreateDirectory(AppsDir);

                //// 1. Проверяем .NET Runtime
                //UpdateStatus("Checking .NET Runtime...");
                //if (!await IsDotNetInstalledAsync())
                //{
                //    UpdateStatus("Installing .NET Runtime...");
                //    await InstallDotNetRuntimeAsync();
                //}

                // 2. Распаковываем приложения
                UpdateStatus("Extracting applications...");
                if (!Directory.Exists(Path.Combine(AppsDir, "WinMasterApp")))
                {
                    await ExtractFromResourcesAsync("WinMasterApp.zip", Path.Combine(AppsDir, "WinMasterApp"));
                }

                // 3. Проверяем обновления
                UpdateStatus("Checking for updates...");
                await CheckUpdatesAsync();

                // Готово к запуску
                UpdateStatus("Ready to launch");
                ProgressBar.Value = 100;
                LaunchButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                MessageBox.Show($"Initialization failed: {ex}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateStatus(string message, int? progress = null)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                if (progress.HasValue)
                {
                    ProgressBar.Value = progress.Value;
                    ProgressText.Text = $"{progress.Value}%";
                }
            });
        }

        private async Task<bool> IsDotNetInstalledAsync()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = "--list-runtimes",
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                return output.Contains("Microsoft.NETCore.App 6.");
            }
            catch
            {
                return false;
            }
        }

        private async Task InstallDotNetRuntimeAsync()
        {
            try
            {
                // Проверяем, есть ли .NET в ресурсах
                var resourceNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
                if (!resourceNames.Contains("WpfLauncher.Resources.dotnet-runtime.zip"))
                {
                    UpdateStatus("Downloading .NET Runtime...");
                    await DownloadDotNetRuntimeAsync();
                }
                else
                {
                    UpdateStatus("Extracting .NET Runtime...");
                    await ExtractFromResourcesAsync("dotnet-runtime.zip", DotnetDir);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to install .NET Runtime: {ex.Message}");
            }
        }

        private async Task DownloadDotNetRuntimeAsync()
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(
                "https://download.visualstudio.microsoft.com/download/pr/abcdefgh-1234-5678-ijkl-mnopqrstuvwx/dotnet-runtime-6.0.0-win-x64.zip");

            response.EnsureSuccessStatusCode();

            var tempFile = Path.Combine(Path.GetTempPath(), "dotnet-runtime.zip");
            using (var fs = new FileStream(tempFile, FileMode.Create))
            {
                await response.Content.CopyToAsync(fs);
            }

            ZipFile.ExtractToDirectory(tempFile, DotnetDir);
            File.Delete(tempFile);
        }

        private async Task ExtractFromResourcesAsync(string resourceName, string targetDir)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullResourceName = $"LauncherApp.Resources.{resourceName}";

            using var stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null)
                throw new FileNotFoundException($"Resource {resourceName} not found");

            var tempFile = Path.Combine(Path.GetTempPath(), resourceName);
            using (var fileStream = new FileStream(tempFile, FileMode.Create))
            {
                await stream.CopyToAsync(fileStream);
            }

            ZipFile.ExtractToDirectory(tempFile, targetDir);
            File.Delete(tempFile);
        }

        private async Task CheckUpdatesAsync()
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", AppName);

                var response = await httpClient.GetStringAsync(
                    $"https://api.github.com/repos/{GitHubRepo}/releases/latest");

                var releaseInfo = JsonConvert.DeserializeObject<GitHubRelease>(response);
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

                if (releaseInfo.TagName != currentVersion)
                {
                    var result = MessageBox.Show(
                        $"New version {releaseInfo.TagName} is available. Update now?",
                        "Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await UpdateApplicationAsync(releaseInfo);
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки при проверке обновлений
            }
        }

        private async Task UpdateApplicationAsync(GitHubRelease release)
        {
            try
            {
                UpdateStatus("Downloading update...", 10);

                var updateDir = Path.Combine(AppDataDir, "Update");
                Directory.CreateDirectory(updateDir);

                // Скачиваем новый лаунчер
                using var httpClient = new HttpClient();
                var newLauncherPath = Path.Combine(updateDir, "Launcher_New.exe");

                using (var response = await httpClient.GetAsync(release.Assets[0].BrowserDownloadUrl))
                using (var fs = new FileStream(newLauncherPath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fs);
                }

                UpdateStatus("Preparing update...", 50);

                // Создаем скрипт обновления
                var batPath = Path.Combine(updateDir, "Updater.bat");
                var batContent = $@"
@echo off
timeout /t 3 /nobreak > nul
taskkill /IM ""Launcher.exe"" /F
move /Y ""{newLauncherPath}"" ""{Path.Combine(AppDataDir, "Launcher.exe")}""
start """" ""{Path.Combine(AppDataDir, "Launcher.exe")}""
del ""%~f0""
";
                File.WriteAllText(batPath, batContent);

                UpdateStatus("Applying update...", 80);

                // Запускаем обновление
                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = updateDir
                });

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update: {ex.Message}", "Update Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var appPath = Path.Combine(AppsDir, "WinMasterApp", "WinMasterApp.exe");
                if (File.Exists(appPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = appPath,
                        UseShellExecute = true,
                        Arguments = "--master"
                    });
                }
                else
                {
                    MessageBox.Show("Application not found", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch application: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class GitHubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; }

        [JsonProperty("assets")]
        public GitHubAsset[] Assets { get; set; }
    }

    public class GitHubAsset
    {
        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }
    }
}