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
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;
using System.Collections.Generic;
using Application = System.Windows.Application;

namespace Kirin_Tool.UI
{
    public partial class ProcessDialogUapp : ContentDialog, INotifyPropertyChanged
    {
        private string _overallStatusText;
        private double _overallProgress;
        private string _currentOperationText;
        private bool _canCancel = true;
        private bool _showCloseButton = false;
        private CancellationTokenSource _cancellationTokenSource;

        public ObservableCollection<FlashPartitionItem> PartitionItems { get; }
        public bool IsUsbUpdateMode { get; set; } = false;

        public event EventHandler RequestClose;

        public string OverallStatusText
        {
            get => _overallStatusText;
            set
            {
                _overallStatusText = value;
                OnPropertyChanged(nameof(OverallStatusText));
            }
        }

        public double OverallProgress
        {
            get => _overallProgress;
            set
            {
                _overallProgress = value;
                OnPropertyChanged(nameof(OverallProgress));
            }
        }

        public string CurrentOperationText
        {
            get => _currentOperationText;
            set
            {
                _currentOperationText = value;
                OnPropertyChanged(nameof(CurrentOperationText));
            }
        }

        public bool CanCancel
        {
            get => _canCancel;
            set
            {
                _canCancel = value;
                OnPropertyChanged(nameof(CanCancel));
            }
        }

        public bool ShowCloseButton
        {
            get => _showCloseButton;
            set
            {
                _showCloseButton = value;
                OnPropertyChanged(nameof(ShowCloseButton));
            }
        }

        public ProcessDialogUapp(List<PartitionInfo> selectedPartitions, CancellationTokenSource cancellationTokenSource)
        {
            InitializeComponent();
            this.DataContext = this;

            _cancellationTokenSource = cancellationTokenSource;

            PartitionItems = new ObservableCollection<FlashPartitionItem>();

            foreach (var partition in selectedPartitions)
            {
                PartitionItems.Add(new FlashPartitionItem(partition));
            }

            OverallStatusText = $"Ready to flash {PartitionItems.Count} partitions";
            CurrentOperationText = "Waiting to start...";
        }

        public ProcessDialogUapp(List<(PartitionInfo Partition, string Source)> partitionsWithSources, CancellationTokenSource cancellationTokenSource)
        {
            InitializeComponent();
            this.DataContext = this;

            _cancellationTokenSource = cancellationTokenSource;

            PartitionItems = new ObservableCollection<FlashPartitionItem>();

            foreach (var (partition, source) in partitionsWithSources)
            {
                PartitionItems.Add(new FlashPartitionItem(partition, source));
            }

            OverallStatusText = $"Ready to flash {PartitionItems.Count} partitions";
            CurrentOperationText = "Waiting to start...";
        }

        public void UpdateCurrentPartitionByIndex(int index, string status, double progress)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (index >= 0 && index < PartitionItems.Count)
                {
                    var item = PartitionItems[index];
                    item.StatusText = status;
                    item.ProgressValue = progress;
                    item.UpdateStatus(status);

                    ScrollToPartition(item);
                }

                if (IsUsbUpdateMode)
                {
                    OverallProgress = progress;
                }
                else
                {
                    var completedItems = PartitionItems.Count(p => p.ProgressValue >= 100);
                    var totalProgress = PartitionItems.Sum(p => p.ProgressValue) / PartitionItems.Count;
                    OverallProgress = totalProgress;
                }

                OverallStatusText = $"Processing partition {index + 1}/{PartitionItems.Count}...";
                CurrentOperationText = $"Flashing: {status}";
            }));
        }

        public void CompletePartitionByIndex(int index, bool success, string message = null)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (index >= 0 && index < PartitionItems.Count)
                {
                    var item = PartitionItems[index];
                    item.Complete(success, message);
                }

                var completedItems = PartitionItems.Count(p => p.IsCompleted);
                OverallStatusText = $"Completed {completedItems}/{PartitionItems.Count} partitions";
            }));
        }


        public void SetOverallStatus(string status)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                OverallStatusText = status;
            }));
        }

        public void UpdateCurrentOperation(string operation)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                CurrentOperationText = operation;
            }));
        }

        public void ReplacePartitions(List<(PartitionInfo Partition, string Source)> newPartitions)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                PartitionItems.Clear();
                foreach (var (partition, source) in newPartitions)
                {
                    PartitionItems.Add(new FlashPartitionItem(partition, source));
                }
                OverallStatusText = $"Ready to flash {PartitionItems.Count} partitions";
            }));
        }



        public void UpdateCurrentPartition(string partitionName, string status, double progress)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var item = PartitionItems.FirstOrDefault(p => p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    item.StatusText = status;
                    item.ProgressValue = progress;
                    item.UpdateStatus(status);

                    ScrollToPartition(item);
                }

                var completedItems = PartitionItems.Count(p => p.ProgressValue >= 100);
                if (IsUsbUpdateMode)
                {
                    OverallProgress = progress;
                }
                else
                {
                    var totalProgress = PartitionItems.Sum(p => p.ProgressValue) / PartitionItems.Count;
                    OverallProgress = totalProgress;
                }

                OverallStatusText = $"Processing {partitionName}... ({completedItems + 1}/{PartitionItems.Count})";
                CurrentOperationText = $"Flashing {partitionName}: {status}";
            }));
        }

        public void CompletePartition(string partitionName, bool success, string message = null)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var item = PartitionItems.FirstOrDefault(p => p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    item.Complete(success, message);
                }

                var completedItems = PartitionItems.Count(p => p.IsCompleted);
                OverallStatusText = $"Completed {completedItems}/{PartitionItems.Count} partitions";
            }));
        }

        public void SetOverallComplete(bool success, string message)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                CanCancel = false;
                ShowCloseButton = true;

                var successCount = PartitionItems.Count(p => p.IsSuccess);
                var totalCount = PartitionItems.Count;

                OverallProgress = 100;

                if (success && successCount == totalCount)
                {
                    OverallStatusText = $"Successfully flashed all {totalCount} partitions!";
                    CurrentOperationText = "Flashing completed successfully.";
                }
                else
                {
                    OverallStatusText = $"Completed with {successCount}/{totalCount} successful";
                    CurrentOperationText = message ?? "Flashing completed with errors.";
                }
            }));
        }

        private void ScrollToPartition(FlashPartitionItem item)
        {
            try
            {
                var index = PartitionItems.IndexOf(item);
                if (index >= 0 && PartitionScrollViewer != null)
                {
                    var itemHeight = 60;
                    var scrollPosition = index * itemHeight;
                    PartitionScrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollPosition - 100));
                }
            }
            catch (Exception ex)
            {
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                CanCancel = false;
                CurrentOperationText = "Cancelling operation and cleaning up...";


                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var item in PartitionItems.Where(p => !p.IsCompleted))
                    {
                        if (item.ProgressValue > 0 && item.ProgressValue < 100)
                        {
                            item.StatusText = "Cancelling...";
                        }
                    }
                });
            }
            catch (Exception ex)
            {
            }
        }


        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequestClose?.Invoke(this, EventArgs.Empty);

                if (this.IsLoaded)
                {
                    this.Visibility = Visibility.Hidden;
                }
            }
            catch (Exception ex)
            {
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
