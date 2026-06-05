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
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;
using Wpf.Ui.Controls;

namespace Kirin_Tool.UI
{
    public partial class ProgressDialog : ContentDialog
    {
        public ObservableCollection<ProgressItemViewModel> ProgressItems { get; }

        public ProgressDialog(ObservableCollection<ProgressItemViewModel> items)
        {
            InitializeComponent();
            ProgressItems = items;
            this.DataContext = this;
        }

        public void UpdateOverallStatus(string status)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OverallStatusTextBlock.Text = status);
            }
            else
            {
                OverallStatusTextBlock.Text = status;
            }
        }

        public void ShowCloseButton(bool isSuccess = true)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() =>
                {
                    CloseButton.Visibility = Visibility.Visible;
                    this.Title = isSuccess ? "Unlock Complete" : "Unlock Failed";
                });
            }
            else
            {
                CloseButton.Visibility = Visibility.Visible;
                this.Title = isSuccess ? "Unlock Complete" : "Unlock Failed";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        public void EnableOKButton(bool isSuccess = true)
        {
            ShowCloseButton(isSuccess);
        }
    }
}
