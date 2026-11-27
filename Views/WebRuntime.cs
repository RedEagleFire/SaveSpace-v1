using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using SaveSpace.Services;

namespace SaveSpace.Views
{
    internal static class WebRuntime
    {
        private static Task<CoreWebView2Environment>? _environment;

        public static Task<CoreWebView2Environment> GetAsync()
        {
            if (_environment != null) return _environment;
            var folder = Path.Combine(AppPaths.Root, "Cache", "WebView2");
            Directory.CreateDirectory(folder);
            _environment = CoreWebView2Environment.CreateAsync(null, folder);
            return _environment;
        }
    }
}
