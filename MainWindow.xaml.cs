// Licensed to Kethily Daniel & NDXCode under one or more agreements.
// Kethily Daniel & NDXCode licenses this file to you under the Business Source License 1.1.
// See the LICENSE file in the project root for more information.

/*
 * Copyright (c) 2026 Kethily Daniel & NDXCode. All rights reserved.
 * 
 * Use of this software is governed by the Business Source License included 
 * in the LICENSE file and at www.mariadb.com/bsl11.
 * 
 * Change Date: Four years from the date each version of the Licensed Work 
 * is first publicly distributed.
 * 
 * On the Change Date, in accordance with the Business Source License, 
 * use of this software will be governed by the GNU General Public License v3.0 
 * or later (GPL-3.0-or-later).
 * 
 * Contact Information: https://kirintool.cfd
 */

using Kirin_Tool.Models;
using Kirin_Tool.Services;
using Kirin_Tool.Services.USBUpdate;
using Kirin_Tool.UI;
using Kirin_Tool.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using Application = System.Windows.Application;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Kirin_Tool
{
    public partial class MainWindow : FluentWindow, INotifyPropertyChanged
    {
        private readonly FastbootClient _fastbootClient;
        private readonly FastbootClient _xmlFastbootClient;
        private readonly FirmwareUnlocker _firmwareUnlocker;
        private readonly OemInfoService _oemInfoService;
        private readonly IContentDialogService _contentDialogService;
        private readonly FastbootFlasherService _fastbootFlasherService;
        private readonly USBUpdateFlasherService _usbUpdateFlasherService;
        private readonly SoftwareTestpointService _swTpService;
        private TaskCompletionSource<bool>? _globalInteractionTcs;

        public FullOtaFile BasePtableFile { get; set; } = new FullOtaFile { FileType = "BasePtable", DisplayName = "Base PTABLE" };
        public FullOtaFile BaseUpdateFile { get; set; } = new FullOtaFile { FileType = "BaseUpdate", DisplayName = "Base UPDATE" };
        public FullOtaFile CustPtableFile { get; set; } = new FullOtaFile { FileType = "CustPtable", DisplayName = "Cust PTABLE" };
        public FullOtaFile CustUpdateFile { get; set; } = new FullOtaFile { FileType = "CustUpdate", DisplayName = "Cust UPDATE" };
        public FullOtaFile PreloadPtableFile { get; set; } = new FullOtaFile { FileType = "PreloadPtable", DisplayName = "Preload PTABLE" };
        public FullOtaFile PreloadUpdateFile { get; set; } = new FullOtaFile { FileType = "PreloadUpdate", DisplayName = "Preload UPDATE" };
        public FullOtaFile SwTpUpdateFile { get; set; } = new FullOtaFile { FileType = "SwTpUpdate", DisplayName = "SW TP Base UPDATE" };
        public FullOtaFile UsbUpdateFile { get; set; } = new FullOtaFile { FileType = "UsbUpdate", DisplayName = "USB Update APP" };

        private bool _isUsbUpdateSelected;
        public bool IsUsbUpdateSelected
        {
            get => _isUsbUpdateSelected;
            set
            {
                if (_isUsbUpdateSelected == value) return;
                _isUsbUpdateSelected = value;
                OnPropertyChanged(nameof(IsUsbUpdateSelected));
                OnPropertyChanged(nameof(IsFastbootUpdateSelected));
                UpdateEditButtonsState();
                OnPropertyChanged(nameof(FullOtaStatusMessage));
                OnPropertyChanged(nameof(ShowFullOtaWarning));
                OnPropertyChanged(nameof(FullOtaWarningMessage));
                RefreshAllPartitionSelections();
            }
        }

        private void UpdateEditButtonsState()
        {
            bool isFastboot = IsFastbootUpdateSelected;
            BasePtableFile.IsEditEnabled = isFastboot && BasePtableFile.HasFile;
            BaseUpdateFile.IsEditEnabled = isFastboot && BaseUpdateFile.HasFile;
            CustPtableFile.IsEditEnabled = false;
            CustUpdateFile.IsEditEnabled = isFastboot && CustUpdateFile.HasFile;
            PreloadPtableFile.IsEditEnabled = false;
            PreloadUpdateFile.IsEditEnabled = isFastboot && PreloadUpdateFile.HasFile;
            SwTpUpdateFile.IsEditEnabled = isFastboot && SwTpUpdateFile.HasFile;
            UsbUpdateFile.IsEditEnabled = isFastboot && UsbUpdateFile.HasFile;
        }

        public bool IsFastbootUpdateSelected
        {
            get => !IsUsbUpdateSelected;
            set => IsUsbUpdateSelected = !value;
        }

        public bool CanStartFullOta => BasePtableFile.HasFile || BaseUpdateFile.HasFile ||
                               CustUpdateFile.HasFile || PreloadUpdateFile.HasFile ||
                               CustPtableFile.HasFile || PreloadPtableFile.HasFile;

        public string FullOtaStatusMessage
        {
            get
            {
                if (IsUsbUpdateSelected)
                {
                    var selectedFiles = new[] { BasePtableFile, CustPtableFile, PreloadPtableFile, BaseUpdateFile, CustUpdateFile, PreloadUpdateFile }
                                       .Count(f => f.HasFile);

                    if (selectedFiles == 0)
                        return "0/6 files selected. Please select at least one file to continue.";
                    else if (selectedFiles == 6)
                        return "6/6 files selected. Ready for complete USB update flash.";
                    else
                        return $"{selectedFiles}/6 files selected. Warning: Incomplete USB update flash";
                }
                else
                {
                    var selectedFiles = new[] { BasePtableFile, BaseUpdateFile, CustUpdateFile, PreloadUpdateFile }
                                       .Count(f => f.HasFile);

                    if (selectedFiles == 0)
                        return "0/4 files selected. Please select at least one file to continue.";
                    else if (selectedFiles == 4)
                        return "4/4 files selected. Ready for complete OTA flash.";
                    else
                        return $"{selectedFiles}/4 files selected. Warning: Incomplete Full OTA flash";
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            _contentDialogService = new ContentDialogService();

            _fastbootClient = new FastbootClient();
            _xmlFastbootClient = new FastbootClient("fastboot/xml/fastboot.exe");
            _firmwareUnlocker = new FirmwareUnlocker();
            _oemInfoService = new OemInfoService(_fastbootClient);
            _fastbootFlasherService = new FastbootFlasherService(_xmlFastbootClient);

            _usbUpdateFlasherService = new USBUpdateFlasherService(
                msg => { }, 
                "dload", 
                async (title, message) => await ShowGlobalInteractionPromptAsync($"{title}: {message}")
            );

            _swTpService = new SoftwareTestpointService(
                msg => { },
                "dload",
                _fastbootClient,
                async (title, message) => await ShowGlobalInteractionPromptAsync($"{title}: {message}")
            );

            SetupNavigationItemHandlers();
            SetupEventHandlers();
            InitializeFullOta();

            ShowWelcomePage();
        }

        private void InitializeFullOta()
        {
            this.DataContext = this;

            BasePtableFile.PropertyChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(CanStartFullOta));
                OnPropertyChanged(nameof(ShowFullOtaWarning));
            };
            BaseUpdateFile.PropertyChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(CanStartFullOta));
                OnPropertyChanged(nameof(ShowFullOtaWarning));
            };
            CustUpdateFile.PropertyChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(CanStartFullOta));
                OnPropertyChanged(nameof(ShowFullOtaWarning));
            };
            PreloadUpdateFile.PropertyChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(CanStartFullOta));
                OnPropertyChanged(nameof(ShowFullOtaWarning));
            };
            UsbUpdateFile.PropertyChanged += (s, e) =>
            {
            };

            BasePtableFile.PropertyChanged += (s, e) => OnPropertyChanged(nameof(FullOtaStatusMessage));
            BaseUpdateFile.PropertyChanged += (s, e) => OnPropertyChanged(nameof(FullOtaStatusMessage));
            CustPtableFile.PropertyChanged += (s, e) => OnPropertyChanged(nameof(FullOtaStatusMessage));
            CustUpdateFile.PropertyChanged += (s, e) => OnPropertyChanged(nameof(FullOtaStatusMessage));
            PreloadPtableFile.PropertyChanged += (s, e) => OnPropertyChanged(nameof(FullOtaStatusMessage));
            PreloadUpdateFile.PropertyChanged += (s, e) => OnPropertyChanged(nameof(FullOtaStatusMessage));

            BasePtableFile.PropertyChanged += FullOtaFile_PropertyChanged;
            BaseUpdateFile.PropertyChanged += FullOtaFile_PropertyChanged;
            CustPtableFile.PropertyChanged += FullOtaFile_PropertyChanged;
            CustUpdateFile.PropertyChanged += FullOtaFile_PropertyChanged;
            PreloadPtableFile.PropertyChanged += FullOtaFile_PropertyChanged;
            PreloadUpdateFile.PropertyChanged += FullOtaFile_PropertyChanged;
            SwTpUpdateFile.PropertyChanged += FullOtaFile_PropertyChanged;
            UsbUpdateFile.PropertyChanged += FullOtaFile_PropertyChanged;
        }

        private void RefreshAllPartitionSelections()
        {
            var files = new[] { BasePtableFile, BaseUpdateFile, CustPtableFile, CustUpdateFile, PreloadPtableFile, PreloadUpdateFile, SwTpUpdateFile, UsbUpdateFile };
            foreach (var file in files)
            {
                if (file.HasFile && File.Exists(file.FilePath))
                {
                    using (var updateApp = new UpdateApp(file.FilePath, IsUsbUpdateSelected))
                    {
                        file.AvailablePartitions = new List<PartitionInfo>(updateApp.Partitions);
                    }
                }

                if (file.AvailablePartitions != null)
                {
                    file.SelectedPartitions = file.AvailablePartitions
                        .Where(p => !IsSkipPartition(p.Name, IsUsbUpdateSelected))
                        .ToList();
                }
            }
        }

        private async void DumpPartitions_Click(object sender, RoutedEventArgs e)
        {
            if (!await _xmlFastbootClient.IsDeviceConnected())
            {
                await ShowMessageBox("Device Not Found", "No device detected in fastboot mode. Please connect your device and try again.");
                return;
            }

            try
            {

                var partitions = await _fastbootFlasherService.GetPartitionTableAsync();


                if (partitions.Count == 0)
                {
                    await ShowMessageBox("No Partitions Found", "No partitions found on the device.");
                    return;
                }

                var partitionSelector = new FastbootPartitionSelector(partitions);
                partitionSelector.SkipSecureCheckBox.Visibility = Visibility.Collapsed;
                var dialogResult = await _contentDialogService.ShowAsync(partitionSelector, CancellationToken.None);


                if (dialogResult != ContentDialogResult.Primary && !partitionSelector.DialogResult)
                {
                    return;
                }

                if (partitionSelector.SelectedPartitions == null)
                {
                    partitionSelector.SetSelectedPartitions();
                }

                var selectedPartitions = partitionSelector.SelectedPartitions;


                if (selectedPartitions == null || selectedPartitions.Count == 0)
                {
                    await ShowMessageBox("No Partitions Selected", "Please select at least one partition to dump.");
                    return;
                }

                var folderDialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select Directory to Save Partitions",
                    ShowNewFolderButton = true
                };

                if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                var saveDirectory = folderDialog.SelectedPath;
                Directory.CreateDirectory(saveDirectory);


                await DumpPartitionsProcess(selectedPartitions, saveDirectory);
            }
            catch (Exception ex)
            {
                await ShowMessageBox("Dump Error", $"Failed to dump partitions: {ex.Message}");
            }
        }

        private List<PartitionInfo> ConvertToPartitionInfo(List<FastbootPartition> fastbootPartitions)
        {
            return fastbootPartitions.Select(fp => new PartitionInfo
            {
                Name = fp.Name,
                Size = 0,
                FormattedSize = "Unknown",
                DataOffset = 0,
                EntryOffset = 0,
                UpdateAppFilePath = fp.DumpPath ?? string.Empty
            }).ToList();
        }

        private async void FlashFromXml_Click(object sender, RoutedEventArgs e)
        {
            if (!await _xmlFastbootClient.IsDeviceConnected())
            {
                await ShowMessageBox("Device Not Found", "No device detected in fastboot mode. Please connect your device and try again.");
                return;
            }

            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Fastboot Configuration XML",
                    Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
                };

                if (openFileDialog.ShowDialog() != true)
                    return;


                var partitions = _fastbootFlasherService.ParseFlashingXml(openFileDialog.FileName);


                if (partitions.Count == 0)
                {
                    await ShowMessageBox("Invalid XML", "No valid partition configurations found in the XML file.");
                    return;
                }

                var missingFiles = partitions.Where(p => !File.Exists(p.DumpPath)).ToList();
                if (missingFiles.Count > 0)
                {
                    var missingList = string.Join("\n", missingFiles.Select(p => $"- {p.DumpPath}"));
                    await ShowMessageBox("Missing Files", $"The following image files were not found:\n\n{missingList}\n\nPlease ensure all image files are accessible from the XML's directory.");
                    return;
                }

                var partitionSelector = new FastbootPartitionSelector(partitions);
                var dialogResult = await _contentDialogService.ShowAsync(partitionSelector, CancellationToken.None);


                if (dialogResult != ContentDialogResult.Primary && !partitionSelector.DialogResult)
                {

                    return;
                }

                if (partitionSelector.SelectedPartitions == null)
                {
                    partitionSelector.SetSelectedPartitions();
                }

                var selectedPartitions = partitionSelector.SelectedPartitions;


                if (selectedPartitions == null || selectedPartitions.Count == 0)
                {
                    await ShowMessageBox("No Partitions Selected", "Please select at least one partition to flash.");
                    return;
                }

                var skipSecure = partitionSelector.SkipSecurePartitions;
                await FlashPartitionsProcess(selectedPartitions, skipSecure);
            }
            catch (Exception ex)
            {
                await ShowMessageBox("Flash Error", $"Failed to flash partitions: {ex.Message}");
            }
        }

        private async Task DumpPartitionsProcess(List<FastbootPartition> selectedPartitions, string dumpDirectory)
        {
            using var cancellationTokenSource = new CancellationTokenSource();

            var partitionInfos = ConvertToPartitionInfo(selectedPartitions);

            var progressDialog = new ProcessDialogUapp(partitionInfos, cancellationTokenSource);

            bool dialogClosed = false;
            progressDialog.RequestClose += (s, e) =>
            {
                dialogClosed = true;
                cancellationTokenSource.Cancel();
            };

            try
            {
                var dialogTask = _contentDialogService.ShowAsync(progressDialog, CancellationToken.None);

                var dumpResult = await Task.Run(async () =>
                {
                    return await DumpPartitionsWithProgress(selectedPartitions, dumpDirectory, progressDialog, cancellationTokenSource.Token);
                }, cancellationTokenSource.Token);

                progressDialog.SetOverallComplete(dumpResult.IsSuccess, dumpResult.Message);

                if (dumpResult.IsSuccess)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        progressDialog.OverallStatusText = $"Successfully dumped all {dumpResult.SuccessCount} partitions!";
                        progressDialog.CurrentOperationText = $"Dumping completed successfully.";
                    });
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        progressDialog.OverallStatusText = $"Completed with {dumpResult.SuccessCount}/{dumpResult.TotalCount} successful";
                        progressDialog.CurrentOperationText = $"Dumping completed with errors.";
                    });
                }

                while (!dialogClosed)
                {
                    await Task.Delay(100);
                    if (dialogTask.IsCompleted) break;
                }

                if (dialogClosed)
                {
                    cancellationTokenSource.Cancel();
                }

                try
                {
                    await Task.WhenAny(dialogTask, Task.Delay(1000));
                }
                catch (OperationCanceledException) { }
            }
            catch (OperationCanceledException)
            {
                progressDialog.SetOverallComplete(false, "Dump operation was cancelled by user");
            }
            catch (Exception ex)
            {
                progressDialog.SetOverallComplete(false, $"Dump failed: {ex.Message}");
            }
        }

        private async Task<(bool IsSuccess, string Message, int SuccessCount, int TotalCount)> DumpPartitionsWithProgress(
        List<FastbootPartition> selectedPartitions,
        string dumpDirectory,
        ProcessDialogUapp progressDialog,
        CancellationToken cancellationToken)
        {
            int successCount = 0;
            int totalCount = selectedPartitions.Count;
            string lastError = string.Empty;
            var dumpedPartitions = new List<FastbootPartition>();

            try
            {
                var fastbootImageDir = Path.Combine(dumpDirectory, "fastbootimage");
                if (!Directory.Exists(fastbootImageDir))
                {
                    Directory.CreateDirectory(fastbootImageDir);
                }

                for (int i = 0; i < selectedPartitions.Count; i++)
                {
                    var partition = selectedPartitions[i];

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();


                        progressDialog.UpdateCurrentPartition(partition.Name, "Dumping...", 50);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            progressDialog.CurrentOperationText = $"Dumping {partition.Name}...";
                        });

                        var dumpPath = Path.Combine(fastbootImageDir, partition.ImageFileName);
                        var result = await _fastbootFlasherService.DumpPartitionAsync(partition.Identifier, dumpPath);

                        if (result.IsSuccess && File.Exists(dumpPath))
                        {
                            progressDialog.CompletePartition(partition.Name, true);
                            partition.DumpPath = dumpPath;
                            dumpedPartitions.Add(partition);
                            successCount++;
                        }
                        else
                        {
                            progressDialog.CompletePartition(partition.Name, false, "Dump failed");
                            lastError = result.Message;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        progressDialog.CompletePartition(partition.Name, false, "Cancelled");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        progressDialog.CompletePartition(partition.Name, false, ex.Message);
                        lastError = ex.Message;
                    }
                }

                if (dumpedPartitions.Count > 0)
                {
                    var xmlPath = Path.Combine(dumpDirectory, "flash.xml");
                    _fastbootFlasherService.GenerateFlashingXml(dumpedPartitions, xmlPath);
                }
            }
            catch (OperationCanceledException)
            {
                return (false, "Dump operation was cancelled by user", successCount, totalCount);
            }

            bool allSuccessful = successCount == totalCount;
            string message = allSuccessful
                ? $"Successfully dumped all {totalCount} partitions to fastbootimage directory!"
                : $"Dump completed with {successCount}/{totalCount} successful.";

            return (allSuccessful, message, successCount, totalCount);
        }


        private async Task FlashPartitionsProcess(List<FastbootPartition> selectedPartitions, bool skipSecure = false)
        {
            using var cancellationTokenSource = new CancellationTokenSource();

            var partitionInfos = ConvertToPartitionInfo(selectedPartitions);

            var progressDialog = new ProcessDialogUapp(partitionInfos, cancellationTokenSource);

            bool dialogClosed = false;
            progressDialog.RequestClose += (s, e) =>
            {
                dialogClosed = true;
                cancellationTokenSource.Cancel();
            };

            try
            {
                var dialogTask = _contentDialogService.ShowAsync(progressDialog, CancellationToken.None);

                var flashResult = await Task.Run(async () =>
                {
                    return await FlashPartitionsWithProgress(selectedPartitions, progressDialog, cancellationTokenSource.Token, skipSecure);
                }, cancellationTokenSource.Token);

                progressDialog.SetOverallComplete(flashResult.IsSuccess, flashResult.Message);

                while (!dialogClosed)
                {
                    await Task.Delay(100);
                    if (dialogTask.IsCompleted) break;
                }

                if (dialogClosed)
                {
                    cancellationTokenSource.Cancel();
                }

                try
                {
                    await Task.WhenAny(dialogTask, Task.Delay(1000));
                }
                catch (OperationCanceledException) { }
            }
            catch (OperationCanceledException)
            {
                progressDialog.SetOverallComplete(false, "Flash operation was cancelled by user");
            }
            catch (Exception ex)
            {
                progressDialog.SetOverallComplete(false, $"Flash failed: {ex.Message}");
            }
        }

        private async Task<(bool IsSuccess, string Message)> FlashPartitionsWithProgress(
        List<FastbootPartition> selectedPartitions,
        ProcessDialogUapp progressDialog,
        CancellationToken cancellationToken,
        bool skipSecure = false)
        {
            int successCount = 0;
            int totalCount = selectedPartitions.Count;
            string lastError = string.Empty;

            try
            {

                for (int i = 0; i < selectedPartitions.Count; i++)
                {
                    var partition = selectedPartitions[i];

                    if (skipSecure && IsSecurePartition(partition.Name, partition.Identifier))
                    {
                        progressDialog.CompletePartition(partition.Name, true, "Skipped");
                        successCount++;
                        continue;
                    }

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();


                        progressDialog.UpdateCurrentPartition(partition.Name, "Flashing...", 50);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            progressDialog.CurrentOperationText = $"Flashing {partition.Name}...";
                        });

                        var result = await _fastbootFlasherService.FlashPartitionAsync(partition.Identifier, partition.DumpPath);

                        if (result.IsSuccess)
                        {
                            progressDialog.CompletePartition(partition.Name, true);
                            successCount++;
                        }
                        else
                        {
                            progressDialog.CompletePartition(partition.Name, false, "Flash failed");
                            lastError = result.Message;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        progressDialog.CompletePartition(partition.Name, false, "Cancelled");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        progressDialog.CompletePartition(partition.Name, false, ex.Message);
                        lastError = ex.Message;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return (false, "Flash operation was cancelled by user");
            }

            bool allSuccessful = successCount == totalCount;
            string message = allSuccessful
                ? $"Successfully flashed all {totalCount} partitions!"
                : $"Flash completed with {successCount}/{totalCount} successful.";

            return (allSuccessful, message);
        }


        public bool ShowFullOtaWarning
        {
            get
            {
                var selectedFiles = IsUsbUpdateSelected
                    ? new[] { BasePtableFile, CustPtableFile, PreloadPtableFile, BaseUpdateFile, CustUpdateFile, PreloadUpdateFile }.Count(f => f.HasFile)
                    : new[] { BasePtableFile, BaseUpdateFile, CustUpdateFile, PreloadUpdateFile }.Count(f => f.HasFile);

                int maxFiles = IsUsbUpdateSelected ? 6 : 4;
                return selectedFiles > 0 && selectedFiles < maxFiles;
            }
        }

        public string FullOtaWarningMessage
        {
            get
            {
                int maxFiles = IsUsbUpdateSelected ? 6 : 4;
                return $"Not all {maxFiles} files are selected. This may result in incomplete firmware flashing.";
            }
        }



        private async void FullOtaFile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FullOtaFile.FilePath) && sender is FullOtaFile otaFile && !string.IsNullOrEmpty(otaFile.FilePath))
            {
                try
                {
                    await Task.Run(() =>
                    {
                        using var updateApp = new UpdateApp(otaFile.FilePath, IsUsbUpdateSelected);
                        otaFile.AvailablePartitions = new List<PartitionInfo>(updateApp.Partitions);

                        otaFile.SelectedPartitions = updateApp.Partitions
                            .Where(p => !IsSkipPartition(p.Name, IsUsbUpdateSelected))
                            .ToList();
                    });
                }
                catch (Exception ex)
                {
                    await ShowMessageBox("Parse Error", $"Failed to parse {otaFile.DisplayName}: {ex.Message}");
                    otaFile.FilePath = null;
                }
            }
        }

        private void SetupNavigationItemHandlers()
        {
            WelcomeItem.Click += (s, e) => HandleNavigation("Welcome");
            DeviceInfoItem.Click += (s, e) => HandleNavigation("DeviceInfo");
            VcomItem.Click += (s, e) => HandleNavigation("Vcom");
            FlashItem.Click += (s, e) => HandleNavigation("Flash");
            SecurityItem.Click += (s, e) => HandleNavigation("Security");
            OEMInfoItem.Click += (s, e) => HandleNavigation("OEMInfo");
            NVMeItem.Click += (s, e) => HandleNavigation("NVMe");
            AboutItem.Click += (s, e) => HandleNavigation("About");
        }

        private void SetupEventHandlers()
        {
            TogLivePull.Checked += TogLivePull_Toggled;
            TogLivePull.Unchecked += TogLivePull_Toggled;
            CpuComboBox.SelectionChanged += CpuComboBox_SelectionChanged;

            _contentDialogService.SetContentPresenter(RootContentDialog);
        }

        private void HandleNavigation(string pageName)
        {
            HideAllPages();
            switch (pageName)
            {
                case "Welcome": ShowWelcomePage(); break;
                case "DeviceInfo": ShowDeviceInfoPage(); break;
                case "Vcom": ShowVcomPage(); break;
                case "Flash": ShowFlashPage(); break;
                case "Security": ShowSecurityPage(); break;
                case "OEMInfo": ShowOEMInfoPage(); break;
                case "NVMe": ShowNVMePage(); break;
                case "About": ShowAboutPage(); break;
            }
        }
        private void ShowWelcomePage() => WelcomePage.Visibility = Visibility.Visible;
        private void ShowDeviceInfoPage() => DeviceInfoPage.Visibility = Visibility.Visible;
        private void ShowVcomPage() => VcomPage.Visibility = Visibility.Visible;
        private void ShowFlashPage() => FlashPage.Visibility = Visibility.Visible;
        private void ShowSecurityPage()
        {
            SecurityPage.Visibility = Visibility.Visible;
            UpdateSecurityUI();
        }
        private void ShowOEMInfoPage()
        {
            OEMInfoPage.Visibility = Visibility.Visible;
            UpdateOemInfoUI();
        }
        private void ShowNVMePage() => NVMePage.Visibility = Visibility.Visible;
        private void ShowAboutPage() => AboutPage.Visibility = Visibility.Visible;
        private void HideAllPages()
        {
            WelcomePage.Visibility = Visibility.Collapsed;
            DeviceInfoPage.Visibility = Visibility.Collapsed;
            VcomPage.Visibility = Visibility.Collapsed;
            FlashPage.Visibility = Visibility.Collapsed;
            SecurityPage.Visibility = Visibility.Collapsed;
            OEMInfoPage.Visibility = Visibility.Collapsed;
            NVMePage.Visibility = Visibility.Collapsed;
            AboutPage.Visibility = Visibility.Collapsed;
        }

        private void BrowseOtaFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.Button button || button.Tag is not string fileType)
                return;

            var openFileDialog = new OpenFileDialog
            {
                Title = $"Select {GetFileDisplayName(fileType)} File",
                Filter = "*.APP files (*.APP)|*.APP|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                GetOtaFileByType(fileType).FilePath = openFileDialog.FileName;
                UpdateEditButtonsState();
            }
        }

        private async void EditOtaFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.Button button || button.Tag is not string fileType)
                return;

            var otaFile = GetOtaFileByType(fileType);
            if (!otaFile.HasFile) return;

            try
            {
                var partitionDialog = new PartitionSelectorDialog(otaFile.AvailablePartitions);

                foreach (var partition in partitionDialog.Partitions)
                {
                    partition.IsSelected = otaFile.SelectedPartitions.Any(sp => sp.Name == partition.Name);
                }

                await _contentDialogService.ShowAsync(partitionDialog, CancellationToken.None);

                if (partitionDialog.DialogResult)
                {
                    partitionDialog.SetSelectedPartitions();
                    otaFile.SelectedPartitions = partitionDialog.SelectedPartitions;
                }
            }
            catch (Exception ex)
            {
                await ShowMessageBox("Edit Error", $"Failed to edit partitions for {otaFile.DisplayName}: {ex.Message}");
            }
        }

        private void ClearOtaFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.Button button || button.Tag is not string fileType)
                return;

            var otaFile = GetOtaFileByType(fileType);
            otaFile.FilePath = null;
            otaFile.AvailablePartitions = new List<PartitionInfo>();
            otaFile.SelectedPartitions = new List<PartitionInfo>();
            UpdateEditButtonsState();
        }

        private async void StartFullOtaButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = new[] { BasePtableFile, BaseUpdateFile, CustUpdateFile, PreloadUpdateFile }
                               .Count(f => f.HasFile);

            if (selectedFiles == 0)
            {
                await ShowMessageBox("No Files Selected", "Please select at least one UPDATE.APP file before starting the flash.");
                return;
            }

            if (selectedFiles < 4)
            {
                var missingFiles = new List<string>();
                if (!BasePtableFile.HasFile) missingFiles.Add("Base PTABLE");
                if (!BaseUpdateFile.HasFile) missingFiles.Add("Base UPDATE");
                if (!CustUpdateFile.HasFile) missingFiles.Add("Cust UPDATE");
                if (!PreloadUpdateFile.HasFile) missingFiles.Add("Preload UPDATE");

                var warningMessage = $"Warning: You have only selected {selectedFiles}/4 files for the Full OTA flash.\n\n" +
                                   $"Missing file(s): {string.Join(", ", missingFiles)}\n\n" +
                                   "Do you want to continue anyway?";

                var warningResult = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
                {
                    Title = "Incomplete Full OTA Flash Warning",
                    Content = warningMessage,
                    PrimaryButtonText = "Continue Anyway",
                    CloseButtonText = "Cancel"
                });

                if (warningResult != ContentDialogResult.Primary)
                {
                    return;
                }
            }

            if (!IsUsbUpdateSelected && !await _fastbootClient.IsDeviceConnected())
            {
                await ShowMessageBox("Device Not Found", "No device detected in fastboot mode. Please connect your device and try again.");
                return;
            }

            if (IsUsbUpdateSelected)
            {
                await StartUsbUpdateFlash();
            }
            else
            {
                await StartFullOtaFlash();
            }
        }

        private async Task StartUsbUpdateFlash()
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            var filesData = new List<(string FilePath, string Label, List<string> SelectedPartitions)>();
            
            var sourceFiles = new[]
            {
                (BasePtableFile, "Base PTABLE"),
                (CustPtableFile, "Cust PTABLE"),
                (PreloadPtableFile, "Preload PTABLE"),
                (BaseUpdateFile, "Base UPDATE"),
                (CustUpdateFile, "Cust UPDATE"),
                (PreloadUpdateFile, "Preload UPDATE")
            };

            var allPartitions = new List<(PartitionInfo Partition, string Source)>();

            foreach (var (file, label) in sourceFiles)
            {
                if (file.HasFile)
                {
                    var selectedNames = file.SelectedPartitions?.Select(p => p.Name).ToList() ?? new List<string>();
                    filesData.Add((file.FilePath, label, selectedNames));

                    if (file.SelectedPartitions != null)
                    {
                        foreach (var partition in file.SelectedPartitions)
                        {
                            allPartitions.Add((partition, label));
                        }
                    }
                }
            }

            if (filesData.Count == 0 || allPartitions.Count == 0)
            {
                await ShowMessageBox("No Files Selected", "Please select at least one APP file to flash.");
                return;
            }

            var flashDialog = new ProcessDialogUapp(allPartitions, cancellationTokenSource);
            flashDialog.IsUsbUpdateMode = true;
            flashDialog.Title = "USB Update Flash Progress";
            bool dialogClosed = false;
            flashDialog.RequestClose += (s, e) =>
            {
                dialogClosed = true;
                cancellationTokenSource.Cancel();
            };

            var flashDialogTask = _contentDialogService.ShowAsync(flashDialog, CancellationToken.None);

            try
            {
                _usbUpdateFlasherService.OnExtractionStarted += (label) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        flashDialog.CurrentOperationText = $"Extracting {label}...";
                        flashDialog.OverallStatusText = $"Preparing files for USB Update flash...";
                    });
                };

                _usbUpdateFlasherService.OnExtractionProgress += (progress) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        flashDialog.OverallProgress = progress;
                    });
                };

                _usbUpdateFlasherService.OnPartitionsDiscovered += (partitions) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var partitionItems = partitions.Select(p => (new PartitionInfo { Name = p.PartitionName }, p.SourceLabel)).ToList();
                        flashDialog.ReplacePartitions(partitionItems);
                    });
                };

                _usbUpdateFlasherService.OnPartitionProgress += (index, name, progress) =>
                {
                    flashDialog.UpdateCurrentPartitionByIndex(index, progress == 100 ? "Finalizing..." : "Processing...", progress);
                };

                _usbUpdateFlasherService.OnPartitionCompleted += (index, name, success, message) =>
                {
                    flashDialog.CompletePartitionByIndex(index, success, message);
                };

                await Task.Run(() => _usbUpdateFlasherService.FlashPartitions(filesData, cancellationTokenSource.Token));

                flashDialog.SetOverallComplete(true, "USB Update flash completed successfully!");
            }
            catch (OperationCanceledException)
            {
                flashDialog.SetOverallComplete(false, "USB Update flash was cancelled by user");
            }
            catch (Exception ex)
            {
                string errorMsg = $"USB Update flash failed: {ex.Message}";
                flashDialog.SetOverallComplete(false, errorMsg);
            }
            finally
            {
                _usbUpdateFlasherService.OnExtractionStarted = null;
                _usbUpdateFlasherService.OnExtractionProgress = null;
                _usbUpdateFlasherService.OnPartitionsDiscovered = null;
                _usbUpdateFlasherService.OnPartitionProgress = null;
                _usbUpdateFlasherService.OnPartitionCompleted = null;

                while (!dialogClosed)
                {
                    await Task.Delay(100);
                    if (flashDialogTask.IsCompleted) break;
                }
            }
        }

        private async Task HandleSoftwareTestpoint(bool enter)
        {
            if (!SwTpUpdateFile.HasFile)
            {
                await ShowMessageBox("File Required", "Please select a Base UPDATE file first.");
                return;
            }

            try
            {
                string action = enter ? "Entering" : "Exiting";
                
                if (enter)
                {
                    await _swTpService.EnterSoftwareTestpoint(SwTpUpdateFile.FilePath);
                    await ShowMessageBox("Success", "Successfully entered VCOM mode!");
                }
                else
                {                    
                    if (!await _fastbootClient.IsDeviceConnected())
                    {
                        await ShowMessageBox("Device Not Found", "Device not detected in fastboot mode.");
                        return;
                    }

                    await _swTpService.ExitSoftwareTestpoint(SwTpUpdateFile.FilePath);
                    await ShowMessageBox("Success", "XLOADER restored successfully!");
                }
            }
            catch (Exception ex)
            {
                await ShowMessageBox("Error", ex.Message);
            }
        }

        private FullOtaFile GetOtaFileByType(string fileType)
        {
            return fileType switch
            {
                "BasePtable" => BasePtableFile,
                "BaseUpdate" => BaseUpdateFile,
                "CustPtable" => CustPtableFile,
                "CustUpdate" => CustUpdateFile,
                "PreloadPtable" => PreloadPtableFile,
                "PreloadUpdate" => PreloadUpdateFile,
                "SwTpUpdate" => SwTpUpdateFile,
                "UsbUpdate" => UsbUpdateFile,
                _ => throw new ArgumentException($"Unknown file type: {fileType}")
            };
        }

        private string GetFileDisplayName(string fileType)
        {
            return fileType switch
            {
                "BasePtable" => "Base PTABLE",
                "BaseUpdate" => "Base UPDATE",
                "CustPtable" => "Cust PTABLE",
                "CustUpdate" => "Cust UPDATE",
                "PreloadPtable" => "Preload PTABLE",
                "PreloadUpdate" => "Preload UPDATE",
                "SwTpUpdate" => "SW TP Base UPDATE",
                "UsbUpdate" => "Base UPDATE",
                _ => fileType
            };
        }

        private async Task StartFullOtaFlash()
        {
            var filesToCheck = new[] { BasePtableFile, BaseUpdateFile, CustUpdateFile, PreloadUpdateFile };
            foreach (var file in filesToCheck)
            {
                if (file.HasFile && !string.IsNullOrEmpty(file.FilePath))
                {
                    string dir = Path.GetDirectoryName(file.FilePath) ?? string.Empty;
                    if (dir.Length + 60 >= 255)
                    {
                        var warningResult = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
                        {
                            Title = "Path Length Warning",
                            Content = $"The folder path for '{file.DisplayName}' is too long\n\n" +
                                      "The flash/extraction process is highly likely to fail.\n\n" +
                                      "Do you want to continue anyway?",
                            PrimaryButtonText = "Continue Anyway",
                            CloseButtonText = "Cancel"
                        });

                        if (warningResult != ContentDialogResult.Primary)
                        {
                            return;
                        }
                        break;
                    }
                }
            }

            using var cancellationTokenSource = new CancellationTokenSource();

            var allPartitions = new List<(PartitionInfo Partition, string Source)>();


            if (BasePtableFile.HasFile && BasePtableFile.SelectedPartitions != null && BasePtableFile.SelectedPartitions.Count > 0)
            {
                foreach (var partition in BasePtableFile.SelectedPartitions)
                {
                    if (IsSkipPartition(partition.Name, false)) continue;
                    allPartitions.Add((partition, "Base PTABLE"));
                }
            }
            else
            {
            }

            if (BaseUpdateFile.HasFile && BaseUpdateFile.SelectedPartitions != null && BaseUpdateFile.SelectedPartitions.Count > 0)
            {
                foreach (var partition in BaseUpdateFile.SelectedPartitions)
                {
                    if (IsSkipPartition(partition.Name, false)) continue;
                    allPartitions.Add((partition, "Base UPDATE"));
                }
            }
            else
            {
            }

            if (CustUpdateFile.HasFile && CustUpdateFile.SelectedPartitions != null && CustUpdateFile.SelectedPartitions.Count > 0)
            {
                foreach (var partition in CustUpdateFile.SelectedPartitions)
                {
                    if (IsSkipPartition(partition.Name, false)) continue;
                    allPartitions.Add((partition, "Cust UPDATE"));
                }
            }
            else
            {
            }

            if (PreloadUpdateFile.HasFile && PreloadUpdateFile.SelectedPartitions != null && PreloadUpdateFile.SelectedPartitions.Count > 0)
            {

                foreach (var partition in PreloadUpdateFile.SelectedPartitions)
                {
                    if (IsSkipPartition(partition.Name, false)) continue;
                    allPartitions.Add((partition, "Preload UPDATE"));
                }
            }
            else
            {
            }


            if (allPartitions.Count == 0)
            {
                await ShowMessageBox("No Partitions", "No partitions selected for flashing. Please edit the file selections.");
                return;
            }

            var selectedFileTypes = new List<string>();
            if (BasePtableFile.HasFile) selectedFileTypes.Add("Base PTABLE");
            if (BaseUpdateFile.HasFile) selectedFileTypes.Add("Base UPDATE");
            if (CustUpdateFile.HasFile) selectedFileTypes.Add("Cust UPDATE");
            if (PreloadUpdateFile.HasFile) selectedFileTypes.Add("Preload UPDATE");


            var flashDialog = new ProcessDialogUapp(allPartitions, cancellationTokenSource);

            bool isComplete = selectedFileTypes.Count == 4;
            flashDialog.Title = isComplete ? "Full OTA Flash Progress" : "Partial OTA Flash Progress";

            bool dialogClosed = false;
            flashDialog.RequestClose += (s, e) =>
            {
                dialogClosed = true;
                cancellationTokenSource.Cancel();
            };

            try
            {
                var flashDialogTask = _contentDialogService.ShowAsync(flashDialog, CancellationToken.None);

                var flashResult = await Task.Run(async () =>
                {
                    return await FlashFullOtaPartitions(allPartitions, flashDialog, cancellationTokenSource.Token);
                }, cancellationTokenSource.Token);

                flashDialog.SetOverallComplete(flashResult.IsSuccess, flashResult.Message);

                while (!dialogClosed)
                {
                    await Task.Delay(100);
                    if (flashDialogTask.IsCompleted) break;
                }

                if (dialogClosed)
                {
                    cancellationTokenSource.Cancel();
                }

                try
                {
                    await Task.WhenAny(flashDialogTask, Task.Delay(1000));
                }
                catch (OperationCanceledException) { }
            }
            catch (OperationCanceledException)
            {
                flashDialog.SetOverallComplete(false, "OTA flash was cancelled by user");
            }
            catch (Exception ex)
            {
                flashDialog.SetOverallComplete(false, $"OTA flash failed: {ex.Message}");
            }
        }


        private async Task<(bool IsSuccess, string Message)> FlashFullOtaPartitions(
        List<(PartitionInfo Partition, string Source)> partitions,
        ProcessDialogUapp progressDialog,
        CancellationToken cancellationToken)
        {
            int successCount = 0;
            int totalCount = partitions.Count;
            string lastError = string.Empty;
            string tempDirectory = null;


            try
            {
                var updateAppPath = partitions.First().Partition.UpdateAppFilePath;
                var updateAppDirectory = Path.GetDirectoryName(updateAppPath);
                tempDirectory = Path.Combine(updateAppDirectory, $"temp_full_ota_{DateTime.Now:yyyyMMdd_HHmmss}");

                if (!Directory.Exists(tempDirectory))
                {
                    Directory.CreateDirectory(tempDirectory);
                }

                var superPartitions = partitions.Where(p => p.Partition.Name.Equals("super", StringComparison.OrdinalIgnoreCase)).ToList();
                if (superPartitions.Count > 1)
                {
                    progressDialog.SetOverallStatus("Merging super partitions...");
                    
                    var mergePaths = new List<string>();
                    for (int i = 0; i < superPartitions.Count; i++)
                    {
                        var (partition, source) = superPartitions[i];
                        string partTempPath = Path.Combine(tempDirectory, $"super_part_{i}.img");
                        progressDialog.UpdateCurrentOperation($"Extracting super part {i+1} from {source}...");
                        await ExtractPartitionToTempDirectoryWithCancellation(partition, partTempPath, cancellationToken);
                        mergePaths.Add(partTempPath);
                    }

                    string mergedSuperPath = Path.Combine(tempDirectory, "super_merged.img");
                    progressDialog.UpdateCurrentOperation("Merging super partitions...");
                    string currentInput = mergePaths[0];
                    for (int i = 1; i < mergePaths.Count; i++)
                    {
                        progressDialog.UpdateCurrentOperation("Merging super partitions...");
                        string nextOutput = i == mergePaths.Count - 1 ? mergedSuperPath : Path.Combine(tempDirectory, $"super_intermediate_{i}.img");
                        await SuperMerger.MergeSuperImages(currentInput, mergePaths[i], nextOutput, p => {
                        });
                        currentInput = nextOutput;
                    }

                    var mergedInfo = new PartitionInfo
                    {
                        Name = "super",
                        Size = new FileInfo(mergedSuperPath).Length,
                        UpdateAppFilePath = mergedSuperPath,
                    };

                    mergedInfo.DataOffset = 0;
                    mergedInfo.UpdateAppFilePath = mergedSuperPath;

                    var firstSuperIndex = partitions.IndexOf(superPartitions[0]);
                    partitions.RemoveAll(p => p.Partition.Name.Equals("super", StringComparison.OrdinalIgnoreCase));
                    partitions.Insert(firstSuperIndex, (mergedInfo, "Merged Super"));

                    progressDialog.ReplacePartitions(partitions);
                    progressDialog.SetOverallStatus("Ready to flash");
                }

                totalCount = partitions.Count;

                for (int i = 0; i < partitions.Count; i++)
                {
                    var (partition, source) = partitions[i];
                    string tempFilePath = null;
                    string uniquePartitionId = $"{partition.Name} ({source})";

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();


                        progressDialog.UpdateCurrentPartitionByIndex(i, source == "Merged Super" ? "Preparing merged image..." : $"Extracting from {source}...", 25);

                        tempFilePath = Path.Combine(tempDirectory, $"{partition.Name}_{source.Replace(" ", "_")}_{i}.img");

                        if (source == "Merged Super" && File.Exists(partition.UpdateAppFilePath))
                        {
                            tempFilePath = partition.UpdateAppFilePath;
                        }
                        else
                        {
                            await ExtractPartitionToTempDirectoryWithCancellation(partition, tempFilePath, cancellationToken);
                        }

                        cancellationToken.ThrowIfCancellationRequested();

                        progressDialog.UpdateCurrentPartitionByIndex(i, $"Flashing from {source}...", 75);

                        string flashPartitionName = partition.Name;
                        var flashResult = await _fastbootClient.FlashPartition(flashPartitionName, tempFilePath);

                        if (flashResult.IsSuccess)
                        {
                            progressDialog.CompletePartitionByIndex(i, true);
                            successCount++;
                        }
                        else
                        {
                            string shortError = flashResult.Output;
                            if (shortError.Length > 200) shortError = shortError.Substring(0, 200) + "...";
                            
                            progressDialog.CompletePartitionByIndex(i, false, $"Failed");
                            lastError = $"Command: fastboot {flashResult.Arguments}\nOutput: {flashResult.Output}";
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        progressDialog.CompletePartitionByIndex(i, false, "Cancelled");

                        if (tempFilePath != null && source != "Merged Super" && File.Exists(tempFilePath))
                        {
                            try { File.Delete(tempFilePath); } catch { }
                        }

                        throw;
                    }
                    catch (Exception ex)
                    {
                        progressDialog.CompletePartitionByIndex(i, false, ex.Message);
                        lastError = ex.Message;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return (false, "Full OTA flash was cancelled by user");
            }
            finally
            {
                if (tempDirectory != null && Directory.Exists(tempDirectory))
                {
                    try
                    {
                        Directory.Delete(tempDirectory, true);

                    }
                    catch (Exception ex)
                    {

                    }
                }
            }

            bool allSuccessful = successCount == totalCount;
            string message = allSuccessful
                ? $"Successfully completed Full OTA flash! ({totalCount} partitions)"
                : $"Full OTA completed with {successCount}/{totalCount} successful.";

            return (allSuccessful, message);
        }


        private async void OperationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button button && button.Tag is string command)
            {
                await ExecuteCommand(command);
            }
        }

        private async Task ExecuteCommand(string command)
        {
            if (command != "unlock-fastboot" && command != "sw-testpoint-enter")
            {
                if (!await _fastbootClient.IsDeviceConnected())
                {
                    await ShowMessageBox("Device Not Found", "No device detected in fastboot mode. Please connect your device and try again.");
                    return;
                }
            }

            try
            {
                switch (command)
                {
                    case "unlock-fastboot":
                        await HandleUnlockFastboot();
                        break;
                    case "frp-remove":
                        await HandleFrpRemove();
                        break;
                    case "read-sn":
                        await HandleReadSerialNumber();
                        break;
                    case "write-sn":
                        await HandleWriteSerialNumber();
                        break;
                    case "unlock-bootloader":
                        await HandleUnlockBootloader();
                        break;
                    case "enable-downgrade":
                        await HandleEnableDowngrade();
                        break;
                    case "reboot-usb-update":
                        await HandleRebootToUsbUpdate();
                        break;
                    case "sw-testpoint-enter":
                        await HandleSoftwareTestpoint(true);
                        break;
                    case "sw-testpoint-exit":
                        await HandleSoftwareTestpoint(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                await ShowMessageBox("Unhandled Exception", $"A critical error occurred: {ex.Message}");
            }
        }

        private async Task HandleUnlockFastboot()
        {
            string cpu = GetSelectedCpu();
            if (cpu == "none" || !FirmwareUnlocker.CpuAddresses.ContainsKey(cpu))
            {
                await ShowMessageBox("CPU Not Supported", "Please select a supported CPU model for the VCOM unlock operation.");
                return;
            }

            var progressItems = new ObservableCollection<ProgressItemViewModel>(
                FirmwareUnlocker.CpuAddresses[cpu].Select(p => new ProgressItemViewModel { FileName = p.Name, StatusText = "Pending" })
            );
            var dialog = new ProgressDialog(progressItems);
            var overallProgress = new Progress<string>(status => dialog.UpdateOverallStatus(status));

            var interactionHandler = new Func<string, Task<bool>>(async message =>
            {
                return await ShowGlobalInteractionPromptAsync(message);
            });

            bool useFastFlashLoader = cpu == "hisi980" && UseFastFlashLoaderSwitch.IsChecked == true;

            var dialogShowTask = _contentDialogService.ShowAsync(dialog, CancellationToken.None);
            var unlockResult = await _firmwareUnlocker.UnlockFastboot(cpu, progressItems, overallProgress, interactionHandler, useFastFlashLoader);

            dialog.ShowCloseButton(unlockResult.IsSuccess);
            await dialogShowTask;
        }

        private async Task<(bool IsSuccess, string Message)> FlashPartitionsWithProgress(List<PartitionInfo> selectedPartitions, ProcessDialogUapp progressDialog, CancellationToken cancellationToken)
        {
            var partitionsToFlash = selectedPartitions
                .Where(p => !IsSkipPartition(p.Name))
                .ToList();

            var skippedPartitions = selectedPartitions.Where(p => IsSkipPartition(p.Name)).ToList();
            foreach (var skipped in skippedPartitions)
            {
            }

            if (partitionsToFlash.Count == 0)
            {
                return (false, "All selected partitions were marked as 'skip' - nothing to flash.");
            }

            int successCount = 0;
            int totalCount = partitionsToFlash.Count;
            string lastError = string.Empty;
            string tempDirectory = null;

            try
            {
                var updateAppPath = partitionsToFlash.First().UpdateAppFilePath;
                var updateAppDirectory = Path.GetDirectoryName(updateAppPath);
                tempDirectory = Path.Combine(updateAppDirectory, $"temp_flash_{DateTime.Now:yyyyMMdd_HHmmss}");

                if (!Directory.Exists(tempDirectory))
                {
                    Directory.CreateDirectory(tempDirectory);
                }

                for (int i = 0; i < partitionsToFlash.Count; i++)
                {
                    var partition = partitionsToFlash[i];
                    string tempFilePath = null;

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        progressDialog.UpdateCurrentPartition(partition.Name, "Extracting...", 25);

                        tempFilePath = Path.Combine(tempDirectory, $"{partition.Name}.img");
                        await ExtractPartitionToTempDirectoryWithCancellation(partition, tempFilePath, cancellationToken);

                        cancellationToken.ThrowIfCancellationRequested();

                        progressDialog.UpdateCurrentPartition(partition.Name, "Flashing...", 75);

                        string flashPartitionName = partition.Name.Equals("efi", StringComparison.OrdinalIgnoreCase) ? "ptable" : partition.Name;
                        var flashResult = await _fastbootClient.FlashPartition(flashPartitionName, tempFilePath);

                        if (flashResult.IsSuccess)
                        {
                            progressDialog.CompletePartition(partition.Name, true);
                            successCount++;
                        }
                        else
                        {
                            progressDialog.CompletePartition(partition.Name, false, "Flash failed");
                            lastError = flashResult.Output;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        progressDialog.CompletePartition(partition.Name, false, "Cancelled");

                        if (tempFilePath != null && File.Exists(tempFilePath))
                        {
                            try
                            {
                                File.Delete(tempFilePath);
                            }
                            catch (Exception ex)
                            {
                            }
                        }

                        throw;
                    }
                    catch (Exception ex)
                    {
                        progressDialog.CompletePartition(partition.Name, false, ex.Message);
                        lastError = ex.Message;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return (false, "Operation was cancelled by user");
            }
            finally
            {
                if (tempDirectory != null && Directory.Exists(tempDirectory))
                {
                    try
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            var files = Directory.GetFiles(tempDirectory);
                            foreach (var file in files)
                            {
                                File.Delete(file);
                            }

                            Directory.Delete(tempDirectory);
                        }
                        catch (Exception ex2)
                        {
                        }
                    }
                }
            }

            bool allSuccessful = successCount == totalCount;
            string message = allSuccessful
                ? $"Successfully flashed all {totalCount} partitions!"
                : $"Completed {successCount}/{totalCount} partitions.";

            return (allSuccessful, message);
        }

        private async Task ExtractPartitionToTempDirectoryWithCancellation(PartitionInfo partition, string outputPath, CancellationToken cancellationToken)
        {
            try
            {
                using var inputStream = new FileStream(partition.UpdateAppFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

                inputStream.Seek(partition.DataOffset, SeekOrigin.Begin);

                const int bufferSize = 65536;
                var buffer = new byte[bufferSize];
                long totalRead = 0;

                while (totalRead < partition.Size)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int toRead = (int)Math.Min(bufferSize, partition.Size - totalRead);
                    int bytesRead = await inputStream.ReadAsync(buffer, 0, toRead, cancellationToken);

                    if (bytesRead == 0)
                        throw new EndOfStreamException($"Unexpected end of file while extracting {partition.Name}");

                    await outputStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    if (totalRead % (bufferSize * 10) == 0)
                    {
                        double extractionProgress = (double)totalRead / partition.Size * 50;
                    }
                }

                await outputStream.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (File.Exists(outputPath))
                {
                    try
                    {
                        File.Delete(outputPath);
                    }
                    catch (Exception ex)
                    {
                    }
                }

                throw;
            }
            catch (Exception ex)
            {
                if (File.Exists(outputPath))
                {
                    try
                    {
                        File.Delete(outputPath);
                    }
                    catch { }
                }

                throw new InvalidOperationException($"Failed to extract partition data for {partition.Name}: {ex.Message}", ex);
            }
        }

        private async Task ExtractPartitionToTempDirectory(PartitionInfo partition, string outputPath)
        {
            try
            {
                using var inputStream = new FileStream(partition.UpdateAppFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

                inputStream.Seek(partition.DataOffset, SeekOrigin.Begin);

                const int bufferSize = 65536;
                var buffer = new byte[bufferSize];
                long totalRead = 0;

                while (totalRead < partition.Size)
                {
                    int toRead = (int)Math.Min(bufferSize, partition.Size - totalRead);
                    int bytesRead = await inputStream.ReadAsync(buffer, 0, toRead);

                    if (bytesRead == 0)
                        throw new EndOfStreamException($"Unexpected end of file while extracting {partition.Name}");

                    await outputStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                }

                await outputStream.FlushAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract partition data for {partition.Name}: {ex.Message}", ex);
            }
        }

        private bool IsSkipPartition(string partitionName, bool isUsbMode = false)
        {
            if (isUsbMode) return false;

            if (string.IsNullOrWhiteSpace(partitionName))
                return true;

            var skipPartitions = new List<string>
            {
                "sha256rsa", "crc", "curver", "verlist", "package_type",
                "base_verlist", "base_ver", "ptable_cust", "cust_verlist",
                "cust_ver", "preload_verlist", "preload_ver", "ptable_preload"
            };

            string lowerName = partitionName.ToLowerInvariant();
            return skipPartitions.Any(skip => lowerName.Contains(skip));
        }

        private bool IsSecurePartition(string name, string identifier)
        {
            var securePartitions = new[] { "modem_secure", "oeminfo", "nvme", "nvm", "modemst1", "modemst2", "fsg", "persist" };
            return securePartitions.Any(p => 
                (name != null && name.Equals(p, StringComparison.OrdinalIgnoreCase)) ||
                (identifier != null && identifier.Equals(p, StringComparison.OrdinalIgnoreCase)));
        }

        private async Task HandleFrpRemove()
        {
            var frpDialog = new FrpStepDialog();
            var dialogTask = _contentDialogService.ShowAsync(frpDialog, CancellationToken.None);

            frpDialog.UpdateOverallStatus("Starting FRP Lock removal...");

            var frpResult = await _fastbootClient.EraseFrpWithSteps();

            await Task.Delay(500);
            frpDialog.UpdateStepStatus(1, frpResult.Step1Success);
            frpDialog.UpdateOverallStatus("Step 1 completed!");

            await Task.Delay(500);
            frpDialog.UpdateStepStatus(2, frpResult.Step2Success);
            frpDialog.UpdateOverallStatus("Step 2 completed!");

            await Task.Delay(500);
            frpDialog.UpdateStepStatus(3, frpResult.Step3Success);

            string finalStatus;
            if (frpResult.AllStepsFailed)
            {
                finalStatus = "FRP Lock removal failed!";
            }
            else if (frpResult.SuccessfulStepsCount == 3)
            {
                finalStatus = "FRP Lock removal completed successfully!";
            }
            else
            {
                finalStatus = "FRP Lock removal completed successfully!";
            }

            frpDialog.UpdateOverallStatus(finalStatus);
            frpDialog.ShowCloseButton(frpResult.OverallSuccess);

            await dialogTask;
        }

        private async Task HandleEnableDowngrade()
        {
            var downgradeDialog = new EnableDowngradeStepDialog();
            var dialogTask = _contentDialogService.ShowAsync(downgradeDialog, CancellationToken.None);

            downgradeDialog.UpdateOverallStatus("Starting Enable Downgrade...");

            var result = await _fastbootClient.EnableDowngradeWithSteps();

            await Task.Delay(500);
            downgradeDialog.UpdateStepStatus(1, result.Step1Success);
            downgradeDialog.UpdateOverallStatus("Step 1 " + (result.Step1Success ? "Completed" : "Failed"));

            await Task.Delay(500);
            downgradeDialog.UpdateStepStatus(2, result.Step2Success);
            downgradeDialog.UpdateOverallStatus("Step 2 " + (result.Step2Success ? "Completed" : "Failed"));

            await Task.Delay(500);
            downgradeDialog.UpdateStepStatus(3, result.Step3Success);
            downgradeDialog.UpdateOverallStatus("Step 3 " + (result.Step3Success ? "Completed" : "Failed"));

            await Task.Delay(500);
            downgradeDialog.UpdateStepStatus(4, result.Step4Success);
            downgradeDialog.UpdateOverallStatus("Step 4 " + (result.Step4Success ? "Completed" : "Failed"));

            string finalStatus;
            if (result.AllStepsFailed)
            {
                finalStatus = "Failed to enable downgrade!";
            }
            else if (result.SuccessfulStepsCount == 4)
            {
                finalStatus = "Downgrade enabled successfully!";
            }
            else
            {
                finalStatus = "Failed to enable downgrade!";
            }

            downgradeDialog.UpdateOverallStatus(finalStatus);
            downgradeDialog.ShowCloseButton(result.OverallSuccess);

            await dialogTask;
        }

        private async Task HandleRebootToUsbUpdate()
        {
            if (!UsbUpdateFile.HasFile)
            {
                await ShowMessageBox("File Required", "Please select a Base UPDATE.APP file first.");
                return;
            }

            if (!await _fastbootClient.IsDeviceConnected())
            {
                await ShowMessageBox("Device Not Found", "No device detected in fastboot mode. Please connect your device and try again.");
                return;
            }

            string lockOutput = await _fastbootClient.OemCommandAsync("lock-state info");
            string fbLockState = ParseOemLine(lockOutput, "FB LockState:", "Unknown");

            using var cancellationTokenSource = new CancellationTokenSource();
            var rescuePartitions = new List<string> { "ERECOVERY_KERNEL", "ERECOVERY_RAMDISK", "ERECOVERY_VENDOR" };
            var partitionInfos = rescuePartitions.Select(p => new PartitionInfo { Name = p, Size = 0, FormattedSize = "" }).ToList();
            var progressDialog = new ProcessDialogUapp(partitionInfos, cancellationTokenSource);
            progressDialog.Title = "Reboot to USB Update Progress";

            bool dialogClosed = false;
            progressDialog.RequestClose += (s, e) =>
            {
                dialogClosed = true;
                cancellationTokenSource.Cancel();
            };

            bool isFbUnlocked = fbLockState.ToUpper().Contains("UNLOCKED");
            Task<ContentDialogResult> dialogTask = null;

            if (isFbUnlocked)
            {
                dialogTask = _contentDialogService.ShowAsync(progressDialog, CancellationToken.None);
                progressDialog.UpdateCurrentOperation("Rebooting to bootloader...");
                await _fastbootClient.RebootBootloaderAsync();
                
                bool deviceReappeared = false;
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(1000);
                    if (await _fastbootClient.IsDeviceConnected())
                    {
                        deviceReappeared = true;
                        break;
                    }
                }

                if (!deviceReappeared)
                {
                    await ShowMessageBox("Timeout", "Device did not reappear in fastboot mode after reboot.");
                    return;
                }
            }

            string tempDir = Path.Combine(Path.GetTempPath(), $"kirintool_usb_update_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                dialogTask ??= _contentDialogService.ShowAsync(progressDialog, CancellationToken.None);

                bool allSuccess = true;
                string baseUpdatePath = UsbUpdateFile.FilePath;

                using (var updateApp = new UpdateApp(baseUpdatePath))
                {
                    for (int i = 0; i < rescuePartitions.Count; i++)
                    {
                        string pName = rescuePartitions[i];
                        var pInfo = updateApp.Partitions.FirstOrDefault(p => p.Name.Equals(pName, StringComparison.OrdinalIgnoreCase));

                        if (pInfo == null)
                        {
                            progressDialog.CompletePartitionByIndex(i, false, "Not found in UPDATE.APP");
                            allSuccess = false;
                            continue;
                        }

                        progressDialog.UpdateCurrentPartitionByIndex(i, "Extracting...", 30);
                        string imgPath = Path.Combine(tempDir, $"{pName}.img");
                        await updateApp.ExtractPartition(pInfo, imgPath);

                        progressDialog.UpdateCurrentPartitionByIndex(i, "Flashing...", 70);
                        string flashCmd = (pName.ToLower()) switch
                        {
                            "erecovery_kernel" => "rescue_recovery_kernel",
                            "erecovery_ramdisk" => "rescue_recovery_ramdisk",
                            "erecovery_vendor" => "rescue_recovery_vendor",
                            _ => pName.ToLower()
                        };

                        var flashResult = await _fastbootClient.FlashPartition(flashCmd, imgPath);
                        if (flashResult.IsSuccess)
                        {
                            progressDialog.CompletePartitionByIndex(i, true);
                        }
                        else
                        {
                            progressDialog.CompletePartitionByIndex(i, false, "Flash failed");
                            allSuccess = false;
                        }
                    }
                }

                if (allSuccess)
                {
                    progressDialog.UpdateCurrentOperation("Rebooting to USB Update mode...");
                    await _fastbootClient.GetVarAsync("rescue_ugs_port");
                    var finalResult = await _fastbootClient.GetVarAsync("rescue_enter_recovery");

                    if (!string.IsNullOrEmpty(finalResult) && 
                        (finalResult.ToUpper().Contains("OKAY") || 
                         finalResult.ToUpper().Contains("SUCCESS") || 
                         finalResult.Contains("This is sec phone!!") || 
                         finalResult.Contains("start to hisuite")))
                    {
                        progressDialog.SetOverallComplete(true, "Rebooted to USB Update mode successfully!");
                    }
                    else
                    {
                        progressDialog.SetOverallComplete(false, $"Flash successful, but failed to enter USB Update mode.");
                    }
                }
                else
                {
                    progressDialog.SetOverallComplete(false, "Failed to prepare or flash rescue partitions.");
                }

                while (!dialogClosed)
                {
                    await Task.Delay(100);
                    if (dialogTask.IsCompleted) break;
                }
            }
            catch (OperationCanceledException)
            {
                progressDialog.SetOverallComplete(false, "Operation cancelled by user.");
            }
            catch (Exception ex)
            {
                progressDialog.SetOverallComplete(false, $"Error: {ex.Message}");
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        private async Task HandleReadSerialNumber()
        {
            var sn = await _fastbootClient.ReadNveVariable("SN");
            CurrentSerialDisplay.Text = string.IsNullOrEmpty(sn) ? "Could not read Serial Number." : sn;
        }

        private async Task HandleWriteSerialNumber()
        {
            var newSn = NewSerialInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(newSn))
            {
                await ShowMessageBox("Input Error", "New Serial Number cannot be empty.");
                return;
            }

            var writeResult = await _fastbootClient.WriteNveVariable("SN", newSn);
            await ShowMessageBox(
                writeResult.IsSuccess ? "Success" : "Error",
                writeResult.IsSuccess ? "Successfully rewrote the device's serial number!" : "Failed to rewrite the device's serial number!"
            );
        }

        private async Task HandleUnlockBootloader()
        {
            if (!await _fastbootClient.IsDeviceConnected())
            {
                await ShowMessageBox("Device Not Found", "No device detected in fastboot mode. Please connect your device and try again.");
                return;
            }

            try
            {
                string fullOutput = string.Empty;
                bool alreadyUnlocked = false;
                var outputLock = new object();
                TaskCompletionSource<bool> alreadyUnlockedDetected = new TaskCompletionSource<bool>();

                var progress = new Progress<string>(output =>
                {
                    lock (outputLock)
                    {
                        fullOutput = output;
                        
                        string lowerOutput = output.ToLower();
                        
                        if (!alreadyUnlocked && (lowerOutput.Contains("already fastboot unlocked") ||
                                                 (lowerOutput.Contains("failed") && lowerOutput.Contains("already") && lowerOutput.Contains("unlocked")) ||
                                                 (lowerOutput.Contains("already") && lowerOutput.Contains("unlocked") && lowerOutput.Contains("remote"))))
                        {
                            alreadyUnlocked = true;
                            alreadyUnlockedDetected.TrySetResult(true);
                        }
                    }
                });

                var unlockTask = _fastbootClient.UnlockBootloaderAsync(progress);

                var alreadyUnlockedTask = alreadyUnlockedDetected.Task;
                var checkTask = await Task.WhenAny(unlockTask, Task.Delay(2000));

                if (alreadyUnlockedTask.IsCompleted && alreadyUnlocked)
                {
                    await ShowMessageBox("Already Unlocked", "The device's bootloader is already unlocked.");
                    return;
                }

                if (checkTask == unlockTask)
                {
                    string currentOutput;
                    lock (outputLock)
                    {
                        currentOutput = fullOutput;
                    }
                    
                    string lowerOutput = currentOutput.ToLower();
                    if (lowerOutput.Contains("already fastboot unlocked") ||
                        (lowerOutput.Contains("failed") && lowerOutput.Contains("already") && lowerOutput.Contains("unlocked")) ||
                        (lowerOutput.Contains("already") && lowerOutput.Contains("unlocked") && lowerOutput.Contains("remote")))
                    {
                        await ShowMessageBox("Already Unlocked", "The device's bootloader is already unlocked.");
                        return;
                    }
                }

                var confirmResult = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
                {
                    Title = "Action Needed",
                    Content = "Please press the POWER button on your device, then click 'I Pressed It'.",
                    PrimaryButtonText = "I Pressed It",
                    CloseButtonText = "Cancel"
                });

                if (confirmResult != ContentDialogResult.Primary)
                {
                    return;
                }

                var unlockResult = await unlockTask;

                string finalOutput = unlockResult.Output;
                string lowerFinalOutput = finalOutput.ToLower();
                
                if (lowerFinalOutput.Contains("already fastboot unlocked") ||
                    (lowerFinalOutput.Contains("failed") && lowerFinalOutput.Contains("already") && lowerFinalOutput.Contains("unlocked")) ||
                    (lowerFinalOutput.Contains("already") && lowerFinalOutput.Contains("unlocked") && lowerFinalOutput.Contains("remote")))
                {
                    await ShowMessageBox("Already Unlocked", "The device's bootloader is already unlocked.");
                    return;
                }
                
                bool isSuccess = finalOutput.Contains("The device will reboot and do factory reset") ||
                                finalOutput.ToUpper().Contains("OKAY");
                
                bool isFailure = finalOutput.ToUpper().Contains("ERROR") ||
                               finalOutput.ToUpper().Contains("FAILED");

                if (isSuccess)
                {
                    await ShowMessageBox("Success", "Your devices bootloader has been unlocked successfully!");
                }
                else if (isFailure)
                {
                    await ShowMessageBox("Failed", "Failed to unlock the bootloader of your device!");
                }
                else
                {
                    await ShowMessageBox("Completed", "Unlock process completed.");
                }
            }
            catch (Exception ex)
            {
                await ShowMessageBox("Unlock Error", $"An error occurred during bootloader unlock: {ex.Message}");
            }
        }

        private async void BtnConvertOem_Click(object sender, RoutedEventArgs e)
        {
            string model = TxtDeviceModel.Text.Trim();
            string vendor = TxtVendor.Text.Trim();
            string selectedCpu = GetSelectedCpu();
            bool useDirectCommand = selectedCpu == "hisi710" || selectedCpu == "hisi710a" || selectedCpu == "hisi980" || selectedCpu == "hisi970" ||
                                    selectedCpu == "hisi810" || selectedCpu == "hisi820" || selectedCpu == "hisi985" || selectedCpu == "hisi990";

            if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(vendor))
            {
                await ShowMessageBox("Input Error", "Device Model and Vendor/Country cannot be empty.");
                return;
            }

            if (useDirectCommand)
            {
                if (!await _fastbootClient.IsDeviceConnected())
                {
                    await ShowMessageBox("Device Not Connected", "No device detected in fastboot mode. Please connect your device and try again.");
                    return;
                }

                try
                {
                    var progressDialog = new ProgressDialog(new ObservableCollection<ProgressItemViewModel>(new[]
                    {
                        new ProgressItemViewModel { FileName = "Writing Model", StatusText = "Pending...", ProgressValue = 0 },
                        new ProgressItemViewModel { FileName = "Writing Vendor", StatusText = "Pending...", ProgressValue = 0 }
                    }));

                    var dialogTask = _contentDialogService.ShowAsync(progressDialog, CancellationToken.None);

                    bool shouldDisableWp = (selectedCpu == "hisi980" && UseFastFlashLoaderSwitch.IsChecked == true) ||
                                           selectedCpu == "hisi810" ||
                                           selectedCpu == "hisi820" ||
                                           selectedCpu == "hisi985" ||
                                           selectedCpu == "hisi990";
                    if (shouldDisableWp)
                    {
                        progressDialog.UpdateOverallStatus("Disabling write protection...");
                        await _fastbootClient.OemCommandAsync("oeminfoerase-disablewp");
                    }

                    progressDialog.UpdateOverallStatus($"Writing model: {model}...");
                    progressDialog.ProgressItems[0].StatusText = "Writing...";
                    progressDialog.ProgressItems[0].ProgressValue = 50;

                    var modelResult = await _fastbootClient.OemCommandAsync($"oeminfowrite-KTModel@{model}");
                    bool modelSuccess = !string.IsNullOrEmpty(modelResult) && 
                                       (modelResult.ToUpper().Contains("OKAY") || 
                                        modelResult.ToUpper().Contains("OK") ||
                                        !modelResult.ToUpper().Contains("FAIL"));

                    progressDialog.ProgressItems[0].StatusText = modelSuccess ? "Done" : "Failed";
                    progressDialog.ProgressItems[0].ProgressValue = modelSuccess ? 100 : 0;

                    if (!modelSuccess)
                    {
                        progressDialog.UpdateOverallStatus($"Failed to write model: {modelResult}");
                        progressDialog.ShowCloseButton(false);
                        await dialogTask;
                        return;
                    }

                    progressDialog.UpdateOverallStatus($"Writing vendor: {vendor}...");
                    progressDialog.ProgressItems[1].StatusText = "Writing...";
                    progressDialog.ProgressItems[1].ProgressValue = 50;

                    var vendorResult = await _fastbootClient.OemCommandAsync($"oeminfowrite-KTVendor@{vendor}");
                    bool vendorSuccess = !string.IsNullOrEmpty(vendorResult) && 
                                        (vendorResult.ToUpper().Contains("OKAY") || 
                                         vendorResult.ToUpper().Contains("OK") ||
                                         !vendorResult.ToUpper().Contains("FAIL"));

                    progressDialog.ProgressItems[1].StatusText = vendorSuccess ? "Done" : "Failed";
                    progressDialog.ProgressItems[1].ProgressValue = vendorSuccess ? 100 : 0;

                    if (modelSuccess && vendorSuccess)
                    {
                        progressDialog.UpdateOverallStatus("Successfully wrote model and vendor to device!");
                        progressDialog.ShowCloseButton(true);
                    }
                    else
                    {
                        progressDialog.UpdateOverallStatus($"Failed to write vendor: {vendorResult}");
                        progressDialog.ShowCloseButton(false);
                    }

                    await dialogTask;
                }
                catch (Exception ex)
                {
                    await ShowMessageBox("Error", $"An error occurred: {ex.Message}");
                }
                return;
            }

            string filePath = TxtOemInfoFile.Text.Trim();
            bool pullFromDevice = TogLivePull.IsChecked == true;

            if (!pullFromDevice && (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)))
            {
                await ShowMessageBox("Input Error", "Please select a valid OEMInfo file.");
                return;
            }

            if (pullFromDevice && !await _fastbootClient.IsDeviceConnected())
            {
                await ShowMessageBox("Device Not Connected", "No device detected in fastboot mode. Please connect your device and try again.");
                return;
            }

            try
            {
                var progressDialog = new ProgressDialog(new ObservableCollection<ProgressItemViewModel>
                {
                    new ProgressItemViewModel { FileName = "OEMInfo Conversion", StatusText = "Starting...", ProgressValue = 0 }
                });

                var dialogTask = _contentDialogService.ShowAsync(progressDialog, CancellationToken.None);
                var progress = new Progress<string>(status =>
                {
                    progressDialog.UpdateOverallStatus(status);
                    progressDialog.ProgressItems[0].StatusText = "Processing";
                    progressDialog.ProgressItems[0].ProgressValue = 50;
                });

                var result = await _oemInfoService.ConvertOemInfo(pullFromDevice, filePath, model, vendor, progress);

                if (!result.IsSuccess)
                {
                    progressDialog.UpdateOverallStatus($"Conversion failed: {result.ErrorMessage}");
                    progressDialog.ProgressItems[0].StatusText = "Failed";
                    progressDialog.ProgressItems[0].ProgressValue = 0;
                    progressDialog.ShowCloseButton(false);
                    await dialogTask;
                    return;
                }

                progressDialog.UpdateOverallStatus("Conversion successful! Press OK to select a save location.");
                progressDialog.ProgressItems[0].StatusText = "Done";
                progressDialog.ProgressItems[0].ProgressValue = 100;
                progressDialog.ShowCloseButton(true);
                await dialogTask;

                var saveDialog = new SaveFileDialog
                {
                    Title = "Save Converted OEMInfo File",
                    Filter = "Image files (*.img)|*.img",
                    FileName = $"converted-oeminfo-{model}.img"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    await File.WriteAllBytesAsync(saveDialog.FileName, result.ConvertedData);

                    if (result.PulledFromDevice)
                    {
                        var flashResult = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
                        {
                            Title = "Flash Back to Device?",
                            Content = "OEMInfo conversion completed successfully!\n\nDo you want to flash the converted OEMInfo back to the device?",
                            PrimaryButtonText = "Yes, Flash",
                            SecondaryButtonText = "No, Keep File Only",
                            CloseButtonText = "Cancel"
                        });

                        if (flashResult == ContentDialogResult.Primary)
                        {
                            await FlashOemInfoToDevice(saveDialog.FileName);
                        }
                        else
                        {
                            await ShowMessageBox("Success", $"Converted OEMInfo saved to:\n{saveDialog.FileName}");
                        }
                    }
                    else
                    {
                        await ShowMessageBox("Success", $"Converted OEMInfo saved to:\n{saveDialog.FileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowMessageBox("Conversion Error", $"An error occurred during conversion: {ex.Message}");
            }
            finally
            {
                _oemInfoService.CleanupTempFiles();
            }
        }

        private async Task FlashOemInfoToDevice(string filePath)
        {
            try
            {
                var progressDialog = new ProgressDialog(new ObservableCollection<ProgressItemViewModel>
                {
                    new ProgressItemViewModel { FileName = "Flashing OEMInfo", StatusText = "Starting...", ProgressValue = 0 }
                });

                var dialogTask = _contentDialogService.ShowAsync(progressDialog, CancellationToken.None);

                progressDialog.UpdateOverallStatus("Flashing OEMInfo to device...");
                progressDialog.ProgressItems[0].StatusText = "Flashing";
                progressDialog.ProgressItems[0].ProgressValue = 50;

                var flashResult = await _oemInfoService.FlashConvertedOemInfo(filePath);

                if (flashResult.IsSuccess && flashResult.Output.ToUpper().Contains("OKAY"))
                {
                    progressDialog.UpdateOverallStatus("OEMInfo flashed successfully!");
                    progressDialog.ProgressItems[0].StatusText = "Done";
                    progressDialog.ProgressItems[0].ProgressValue = 100;
                    progressDialog.ShowCloseButton(true);
                }
                else
                {
                    progressDialog.UpdateOverallStatus("Failed to flash OEMInfo to device.");
                    progressDialog.ProgressItems[0].StatusText = "Failed";
                    progressDialog.ProgressItems[0].ProgressValue = 0;
                    progressDialog.ShowCloseButton(false);
                }

                await dialogTask;
            }
            catch (Exception ex)
            {
                await ShowMessageBox("Flash Error", $"Error flashing OEMInfo: {ex.Message}");
            }
        }

        private void BrowseOemInfoFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog { Title = "Select OEMInfo File", Filter = "Image files (*.img)|*.img|All files (*.*)|*.*" };
            if (openFileDialog.ShowDialog() == true)
            {
                TxtOemInfoFile.Text = openFileDialog.FileName;
            }
        }

        private void TogLivePull_Toggled(object sender, RoutedEventArgs e)
        {
            FileSelectionPanel.IsEnabled = !(TogLivePull.IsChecked == true);
            if (TogLivePull.IsChecked == true) TxtOemInfoFile.Text = "";
        }

        private void CpuComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateOemInfoUI();
            UpdateSecurityUI();
            
            if (UseFastFlashLoaderSwitch != null)
            {
                UseFastFlashLoaderSwitch.Visibility = GetSelectedCpu() == "hisi980" ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateOemInfoUI()
        {
            string selectedCpu = GetSelectedCpu();
            bool useDirectCommand = selectedCpu == "hisi710" || selectedCpu == "hisi710a" || selectedCpu == "hisi980" || selectedCpu == "hisi970" ||
                                    selectedCpu == "hisi810" || selectedCpu == "hisi820" || selectedCpu == "hisi985" || selectedCpu == "hisi990";

            if (useDirectCommand)
            {
                TogLivePull.Visibility = Visibility.Collapsed;
                FileSelectionPanel.Visibility = Visibility.Collapsed;
                devSwtxt.Visibility = Visibility.Collapsed;
            }
            else
            {
                TogLivePull.Visibility = Visibility.Visible;
                FileSelectionPanel.Visibility = Visibility.Visible;
                devSwtxt.Visibility = Visibility.Visible;
            }
        }

        private void UpdateSecurityUI()
        {
            string selectedCpu = GetSelectedCpu();
            bool isSupported = selectedCpu == "hisi710" || selectedCpu == "hisi970" || selectedCpu == "hisi980" || selectedCpu == "hisi990";

            if (UnlockBootloaderPanel != null)
            {
                UnlockBootloaderPanel.Visibility = isSupported ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private string GetSelectedCpu()
        {
            if (CpuComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Content.ToString().Contains('('))
            {
                string content = selectedItem.Content.ToString();
                int start = content.IndexOf('(') + 1;
                int end = content.IndexOf(')');
                return content.Substring(start, end - start);
            }
            return "none";
        }

        private async Task ShowMessageBox(string title, string message)
        {
            await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions()
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK"
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer", "https://kirintool.cfd");

        }

        private void TelegramButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer", "https://t.me/kirintoolsupport");
        }

        private void GithubButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer", "https://github.com/kethily-daniel/kirin-tool");
        }
        private async Task<bool> ShowGlobalInteractionPromptAsync(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                return await Dispatcher.InvokeAsync(() => ShowGlobalInteractionPromptAsync(message)).Task.Result;
            }

            InteractionMessageTextBlock.Text = message;
            GlobalInteractionOverlay.Visibility = Visibility.Visible;

            _globalInteractionTcs = new TaskCompletionSource<bool>();
            bool result = await _globalInteractionTcs.Task;

            GlobalInteractionOverlay.Visibility = Visibility.Collapsed;
            return result;
        }

        private void GlobalInteractionDone_Click(object sender, RoutedEventArgs e)
        {
            _globalInteractionTcs?.TrySetResult(true);
        }

        private void GlobalInteractionCancel_Click(object sender, RoutedEventArgs e)
        {
            _globalInteractionTcs?.TrySetResult(false);
        }

        private async void ReadDeviceInfo_Click(object sender, RoutedEventArgs e)
        {
            if (!await _fastbootClient.IsDeviceConnected())
            {
                await ShowMessageBox("Device Not Found", "No device detected in fastboot mode. Please connect your device and try again.");
                return;
            }

            InfoFbLock.Text = "Reading...";
            InfoUserLock.Text = "Reading...";
            InfoDeviceModel.Text = "Reading...";
            InfoVendorCountry.Text = "Reading...";
            InfoBuildNumber.Text = "Reading...";
            InfoBaseVersion.Text = "Reading...";
            InfoCustomVersion.Text = "Reading...";
            InfoPreloadVersion.Text = "Reading...";

            try
            {
                string lockOutput = await _fastbootClient.OemCommandAsync("lock-state info");
                InfoFbLock.Text = ParseOemLine(lockOutput, "FB LockState:", "Unknown");
                InfoUserLock.Text = ParseOemLine(lockOutput, "USER LockState:", "Unknown");
            }
            catch { InfoFbLock.Text = "Unknown"; InfoUserLock.Text = "Unknown"; }

            await Task.Delay(300);

            try
            {
                InfoDeviceModel.Text = ParseGetVar(await _fastbootClient.GetVarAsync("devicemodel"), "Unknown");
            }
            catch { InfoDeviceModel.Text = "Unknown"; }

            await Task.Delay(300);

            try
            {
                InfoVendorCountry.Text = ParseGetVar(await _fastbootClient.GetVarAsync("vendorcountry"), "Unknown");
            }
            catch { InfoVendorCountry.Text = "Unknown"; }

            await Task.Delay(300);

            try
            {
                InfoBaseVersion.Text = ParseOemVersion(await _fastbootClient.OemCommandAsync("oeminforead-BASE_VERSION"));
            }
            catch { InfoBaseVersion.Text = "Unknown"; }

            await Task.Delay(300);

            try
            {
                InfoCustomVersion.Text = ParseOemVersion(await _fastbootClient.OemCommandAsync("oeminforead-CUSTOM_VERSION"));
            }
            catch { InfoCustomVersion.Text = "Unknown"; }

            await Task.Delay(300);

            try
            {
                InfoPreloadVersion.Text = ParseOemVersion(await _fastbootClient.OemCommandAsync("oeminforead-PRELOAD_VERSION"));
            }
            catch { InfoPreloadVersion.Text = "Unknown"; }

            await Task.Delay(300);

            try
            {
                string buildOutput = await _fastbootClient.OemCommandAsync("get-build-number");
                string rawBuild = ParseOemVersion(buildOutput);
                if (rawBuild != "Unknown" && rawBuild.Contains(" "))
                    InfoBuildNumber.Text = rawBuild.Substring(rawBuild.IndexOf(' ') + 1);
                else
                    InfoBuildNumber.Text = rawBuild;
            }
            catch { InfoBuildNumber.Text = "Unknown"; }

            if (DeviceInfoExpander != null)
            {
                DeviceInfoExpander.IsExpanded = true;
            }
        }

        private string ParseOemLine(string output, string key, string fallback)
        {
            if (string.IsNullOrEmpty(output)) return fallback;
            
            var line = output.Split('\n', '\r')
                             .Select(l => l.Trim())
                             .FirstOrDefault(l => l.Contains(key));
                             
            if (line != null)
            {
                int keyIndex = line.IndexOf(key);
                return line.Substring(keyIndex + key.Length).Trim();
            }
            return fallback;
        }

        private string ParseGetVar(string output, string fallback)
        {
            if (string.IsNullOrEmpty(output)) return fallback;

            var line = output.Split('\n', '\r')
                             .Select(l => l.Trim())
                             .FirstOrDefault(l => l.Contains(":") && !l.StartsWith("finished") && !l.StartsWith("OKAY"));
                             
            if (line != null)
            {
                return line.Substring(line.IndexOf(':') + 1).Trim();
            }
            return fallback;
        }

        private string ParseOemVersion(string output)
        {
            if (string.IsNullOrEmpty(output)) return "Unknown";

            var line = output.Split('\n', '\r')
                             .Select(l => l.Trim())
                             .FirstOrDefault(l => l.Contains("(bootloader)") && l.Contains(":") && !l.EndsWith("s") && !l.Contains("total time") && !l.Contains("Error"));
            
            if (line != null)
            {
                int colonIndex = line.LastIndexOf(':');
                if (colonIndex != -1)
                {
                    string value = line.Substring(colonIndex + 1).Trim();
                    return string.IsNullOrEmpty(value) ? "Unknown" : value;
                }
            }
            
            return "Unknown";
        }

        private async void RebootDevice_Click(object sender, RoutedEventArgs e)
        {
            if (!await _fastbootClient.IsDeviceConnected())
            {
                await ShowMessageBox("Device Not Found", "No device detected in fastboot mode. Please connect your device and try again.");
                return;
            }

            var result = await _fastbootClient.RebootAsync();
            if (result.IsSuccess)
            {
                await ShowMessageBox("Success", "Rebooted successfully!");
            }
            else
            {
                await ShowMessageBox("Error", $"Failed to reboot device: {result.Output}");
            }
        }
    }
}
