using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Newtonsoft.Json;
using Ookii.Dialogs.Wpf;
using MessageBox = System.Windows.MessageBox;
using System.Xml;

namespace SimpleInternetBlock
{
    public partial class MainWindow : Window
    {
        private const string CONFIG_FILE = "blocked_items.json";
        private List<BlockedItem> blockedItems = new List<BlockedItem>();

        public MainWindow()
        {
            InitializeComponent();
            LoadBlockedItems();
            RefreshListBox();

            // Set placeholder text
            txtPath.Text = "Enter file or folder path...";
            txtPath.GotFocus += (s, e) => { if (txtPath.Text == "Enter file or folder path...") txtPath.Text = ""; };
            txtPath.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(txtPath.Text)) txtPath.Text = "Enter file or folder path..."; };
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (chkIsFolder.IsChecked == true)
            {
                var dialog = new VistaFolderBrowserDialog
                {
                    Description = "Select a folder to block all executables",
                    UseDescriptionForTitle = true
                };

                if (dialog.ShowDialog() == true)
                {
                    txtPath.Text = dialog.SelectedPath;
                }
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                    Title = "Select an application to block"
                };

                if (dialog.ShowDialog() == true)
                {
                    txtPath.Text = dialog.FileName;
                }
            }
        }

        private void BtnBlock_Click(object sender, RoutedEventArgs e)
        {
            string path = txtPath.Text.Trim();

            if (string.IsNullOrEmpty(path) || path == "Enter file or folder path...")
            {
                MessageBox.Show("Please enter a path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool isFolder = chkIsFolder.IsChecked == true;

            if (!isFolder && !File.Exists(path))
            {
                MessageBox.Show("File does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (isFolder && !Directory.Exists(path))
            {
                MessageBox.Show("Folder does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var item = new BlockedItem { Path = path, IsFolder = isFolder };

            if (blockedItems.Any(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("This path is already blocked.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                int count = 0;
                if (isFolder)
                {
                    count = BlockFolder(path);
                }
                else
                {
                    BlockApplication(path);
                    count = 1;
                }

                blockedItems.Add(item);
                SaveBlockedItems();
                RefreshListBox();
                txtPath.Text = "Enter file or folder path...";
                txtStatus.Text = $"✓ Successfully blocked {count} application(s): {System.IO.Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "✗ Failed to block item.";
            }
        }

        private void BtnUnblock_Click(object sender, RoutedEventArgs e)
        {
            if (lstBlocked.SelectedIndex == -1)
            {
                MessageBox.Show("Please select an item to unblock.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedItem = blockedItems[lstBlocked.SelectedIndex];

            var result = MessageBox.Show(
                $"Are you sure you want to unblock:\n{selectedItem.Path}?",
                "Confirm Unblock",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                int count = 0;
                if (selectedItem.IsFolder)
                {
                    count = UnblockFolder(selectedItem.Path);
                }
                else
                {
                    UnblockApplication(selectedItem.Path);
                    count = 1;
                }

                blockedItems.RemoveAt(lstBlocked.SelectedIndex);
                SaveBlockedItems();
                RefreshListBox();
                txtStatus.Text = $"✓ Successfully unblocked {count} application(s).";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "✗ Failed to unblock item.";
            }
        }

        private void BlockApplication(string exePath)
        {
            string ruleName = $"BlockApp_{System.IO.Path.GetFileName(exePath)}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

            RunNetshCommand($"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block program=\"{exePath}\"");
            RunNetshCommand($"advfirewall firewall add rule name=\"{ruleName}_In\" dir=in action=block program=\"{exePath}\"");
        }

        private int BlockFolder(string folderPath)
        {
            var exeFiles = Directory.GetFiles(folderPath, "*.exe", SearchOption.AllDirectories);

            foreach (var exePath in exeFiles)
            {
                BlockApplication(exePath);
            }

            if (exeFiles.Length == 0)
            {
                MessageBox.Show("No executable files found in this folder.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return exeFiles.Length;
        }

        private void UnblockApplication(string exePath)
        {
            // Use wildcard deletion for rules containing the executable path
            string fileName = System.IO.Path.GetFileName(exePath);

            // Delete all rules that match this application
            RunNetshCommand($"advfirewall firewall delete rule name=all program=\"{exePath}\"");
        }

        private int UnblockFolder(string folderPath)
        {
            var exeFiles = Directory.GetFiles(folderPath, "*.exe", SearchOption.AllDirectories);

            foreach (var exePath in exeFiles)
            {
                UnblockApplication(exePath);
            }

            return exeFiles.Length;
        }

        private void RunNetshCommand(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = Process.Start(psi))
            {
                process?.WaitForExit();
            }
        }

        private List<string> GetFirewallRules()
        {
            var rules = new List<string>();
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "advfirewall firewall show rule name=all",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(psi))
            {
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    rules.AddRange(output.Split(new[] { "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries));
                }
            }

            return rules;
        }

        private string ExtractRuleName(string ruleBlock)
        {
            var lines = ruleBlock.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("Rule Name:", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring("Rule Name:".Length).Trim();
                }
            }
            return null;
        }

        private void RefreshListBox()
        {
            lstBlocked.Items.Clear();
            foreach (var item in blockedItems)
            {
                string icon = item.IsFolder ? "📁" : "📄";
                string type = item.IsFolder ? "FOLDER" : "APP";
                lstBlocked.Items.Add($"{icon} [{type}] {item.Path}");
            }
        }

        private void SaveBlockedItems()
        {
            var json = JsonConvert.SerializeObject(blockedItems, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(CONFIG_FILE, json);
        }

        private void LoadBlockedItems()
        {
            if (File.Exists(CONFIG_FILE))
            {
                var json = File.ReadAllText(CONFIG_FILE);
                blockedItems = JsonConvert.DeserializeObject<List<BlockedItem>>(json) ?? new List<BlockedItem>();
            }
        }
    }

    public class BlockedItem
    {
        public string Path { get; set; }
        public bool IsFolder { get; set; }
    }
}