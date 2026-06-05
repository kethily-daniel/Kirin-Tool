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

namespace Kirin_Tool.Models
{
    public class EnableDowngradeResult
    {
        public bool Step1Success { get; set; }
        public string Step1Output { get; set; } = "";

        public bool Step2Success { get; set; }
        public string Step2Output { get; set; } = "";

        public bool Step3Success { get; set; }
        public string Step3Output { get; set; } = "";

        public bool Step4Success { get; set; }
        public string Step4Output { get; set; } = "";

        public bool OverallSuccess => Step1Success || Step2Success || Step3Success || Step4Success;

        public bool AllStepsFailed => !Step1Success && !Step2Success && !Step3Success && !Step4Success;

        public int SuccessfulStepsCount =>
            (Step1Success ? 1 : 0) +
            (Step2Success ? 1 : 0) +
            (Step3Success ? 1 : 0) +
            (Step4Success ? 1 : 0);
    }
}
