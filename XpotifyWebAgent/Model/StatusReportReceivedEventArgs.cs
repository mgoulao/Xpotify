﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XpotifyWebAgent.Model
{
    public sealed class StatusReportReceivedEventArgs
    {
        public WebAppStatus Status { get; set; }
    }
}
