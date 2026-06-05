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

using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace Kirin_Tool.UI
{
    public partial class EnableDowngradeStepDialog : ContentDialog
    {
        public EnableDowngradeStepDialog()
        {
            InitializeComponent();
        }

        public void UpdateStepStatus(int stepNumber, bool success)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateStepStatusInternal(stepNumber, success));
            }
            else
            {
                UpdateStepStatusInternal(stepNumber, success);
            }
        }

        private void UpdateStepStatusInternal(int stepNumber, bool success)
        {
            SymbolIcon icon = stepNumber switch
            {
                1 => Step1Icon,
                2 => Step2Icon,
                3 => Step3Icon,
                4 => Step4Icon,
                _ => null
            };

            if (icon != null)
            {
                if (success)
                {
                    icon.Symbol = SymbolRegular.Checkmark24;
                    icon.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    icon.Symbol = SymbolRegular.Dismiss24;
                    icon.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
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
                    this.Title = isSuccess ? "Downgrade enabled successfully" : "Failed to enable downgrade";
                });
            }
            else
            {
                CloseButton.Visibility = Visibility.Visible;
                this.Title = isSuccess ? "Downgrade enabled successfully" : "Failed to enable downgrade";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }
    }
}
