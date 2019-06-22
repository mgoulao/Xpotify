﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XpotifyWebAgent.Model
{
    public sealed class WebAppStatus
    {
        public bool BackButtonEnabled { get; set; }
        public NowPlayingData NowPlaying { get; set; }
    }
}
