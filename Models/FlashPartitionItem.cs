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
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;

namespace Kirin_Tool.UI
{
    public class FlashPartitionItem : INotifyPropertyChanged
    {
        private string _statusText = "Pending";
        private double _progressValue = 0;
        private bool _isCompleted = false;
        private bool _isSuccess = false;
        private Stopwatch _stopwatch;
        private DispatcherTimer _tickTimer;
        private string _timeElapsed = "";

        public PartitionInfo Partition { get; }
        public string Name { get; }
        public string FormattedSize { get; }
        public string DisplayName { get; set; }
        public string UniqueId { get; }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;
                OnPropertyChanged(nameof(ProgressValue));
            }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                _isCompleted = value;
                OnPropertyChanged(nameof(IsCompleted));
            }
        }

        public bool IsSuccess
        {
            get => _isSuccess;
            set
            {
                _isSuccess = value;
                OnPropertyChanged(nameof(IsSuccess));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(BackgroundBrush));
                OnPropertyChanged(nameof(BorderBrush));
            }
        }

        public string TimeElapsed
        {
            get => _timeElapsed;
            set
            {
                _timeElapsed = value;
                OnPropertyChanged(nameof(TimeElapsed));
            }
        }

        public SolidColorBrush StatusColor
        {
            get
            {
                if (IsCompleted)
                    return IsSuccess ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);

                if (ProgressValue > 0)
                    return new SolidColorBrush(Colors.Orange);

                return new SolidColorBrush(Colors.Gray);
            }
        }

        public SolidColorBrush BackgroundBrush
        {
            get
            {
                if (IsCompleted)
                    return IsSuccess ? new SolidColorBrush(Color.FromRgb(240, 255, 240)) : new SolidColorBrush(Color.FromRgb(255, 240, 240));

                if (ProgressValue > 0)
                    return new SolidColorBrush(Color.FromRgb(255, 250, 230));

                return new SolidColorBrush(Colors.White);
            }
        }

        public SolidColorBrush BorderBrush
        {
            get
            {
                if (IsCompleted)
                    return IsSuccess ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);

                if (ProgressValue > 0)
                    return new SolidColorBrush(Colors.Orange);

                return new SolidColorBrush(Colors.LightGray);
            }
        }

        public FlashPartitionItem(PartitionInfo partition)
        {
            Partition = partition;
            Name = partition.Name;
            FormattedSize = partition.FormattedSize;
            DisplayName = partition.Name;
            UniqueId = $"{partition.Name}_{partition.EntryOffset}";
            _stopwatch = new Stopwatch();
            InitializeTickTimer();
        }

        public FlashPartitionItem(PartitionInfo partition, string source)
        {
            Partition = partition;
            Name = partition.Name;
            FormattedSize = partition.FormattedSize;
            DisplayName = $"{partition.Name} ({source})";
            UniqueId = $"{partition.Name}_{partition.EntryOffset}";
            _stopwatch = new Stopwatch();
            InitializeTickTimer();
        }

        private void InitializeTickTimer()
        {
            _tickTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _tickTimer.Tick += (s, e) =>
            {
                if (_stopwatch != null && _stopwatch.IsRunning)
                {
                    TimeElapsed = _stopwatch.Elapsed.ToString(@"mm\:ss");
                }
            };
        }

        private void StartTimer()
        {
            if (!_stopwatch.IsRunning)
            {
                _stopwatch.Start();
            }
            if (!_tickTimer.IsEnabled)
            {
                _tickTimer.Start();
            }
        }

        private void StopTimer()
        {
            _tickTimer?.Stop();
            _stopwatch?.Stop();
        }

        public void UpdateStatus(string status)
        {
            if (ProgressValue > 0 && !_isCompleted)
            {
                StartTimer();
            }

            if (_stopwatch.IsRunning)
            {
                TimeElapsed = _stopwatch.Elapsed.ToString(@"mm\:ss");
            }

            StatusText = status;
        }

        public void Complete(bool success, string message = null)
        {
            StopTimer();
            IsCompleted = true;
            IsSuccess = success;
            ProgressValue = 100;
            StatusText = success ? "Done" : (message ?? "Failed");
            TimeElapsed = _stopwatch?.Elapsed.ToString(@"mm\:ss") ?? "";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
