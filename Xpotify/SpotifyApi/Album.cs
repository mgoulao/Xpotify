﻿using Xpotify.Helpers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Xpotify.SpotifyApi
{
    public class Album : ApiBase
    {
        public async Task<Model.AlbumSimplified> GetAlbum(string albumId)
        {
            var result = await SendRequestWithTokenAsync($"https://api.spotify.com/v1/albums/{albumId}", HttpMethod.Get);
            var resultString = await result.Content.ReadAsStringAsync();

            if (result.IsSuccessStatusCode == false)
                AnalyticsHelper.Log("api", "getalbum::" + result.StatusCode.ToString());

            return JsonConvert.DeserializeObject<Model.AlbumSimplified>(resultString);
        }

    }
}
