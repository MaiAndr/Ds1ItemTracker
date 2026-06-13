namespace Ds1ItemTracker.Services;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Linq;

public sealed record OverlayItem(string Name, bool PickedUp);

public sealed class OverlaySnapshot
{
    public string AreaName          { get; init; } = "—";
    public int    PickedUp          { get; init; }
    public int    Total             { get; init; }
    public int    TotalPickedUp     { get; init; }
    public int    TotalItems        { get; init; }
    public double BgOpacity         { get; init; } = 0.55;
    public int    FontSize          { get; init; } = 11;
    public string FontColor         { get; init; } = "#E0D5C0";
    public int    FontOpacity       { get; init; } = 100;
    public string BgColor           { get; init; } = "#0D0D0D";
    public int    Columns           { get; init; } = 1;
    public IReadOnlyList<OverlayItem> Items { get; init; } = [];
    public static readonly OverlaySnapshot Empty = new();
}

/// <summary>
/// Tiny embedded HTTP server that serves a stream-overlay HTML page and a
/// /state.json endpoint. Access at http://localhost:7373/ in OBS browser source.
/// </summary>
public sealed class OverlayServer : IDisposable
{
    private readonly HttpListener _listener;
    private OverlaySnapshot _state = OverlaySnapshot.Empty;
    private readonly object _lock  = new();
    private volatile bool   _running;

    public int    Port { get; }
    public string Url  => $"http://localhost:{Port}/";

    public OverlayServer(int port = 7373)
    {
        Port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void UpdateState(OverlaySnapshot state)
    {
        lock (_lock) _state = state;
    }

    /// <summary>Returns false if the port is already in use.</summary>
    public bool Start()
    {
        try { _listener.Start(); _running = true; Task.Run(ListenLoop); return true; }
        catch { return false; }
    }

    private async Task ListenLoop()
    {
        while (_running && _listener.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => Serve(ctx));
            }
            catch when (!_running) { break; }
            catch { }
        }
    }

    private void Serve(HttpListenerContext ctx)
    {
        try
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            bool isState = (ctx.Request.Url?.AbsolutePath ?? "/")
                           .Contains("state", StringComparison.OrdinalIgnoreCase);
            byte[] data;
            string ct;

            if (isState)
            {
                OverlaySnapshot snap;
                lock (_lock) snap = _state;

                string json = JsonSerializer.Serialize(new
                {
                    areaName     = snap.AreaName,
                    pickedUp     = snap.PickedUp,
                    total        = snap.Total,
                    totalPickedUp = snap.TotalPickedUp,
                    totalItems   = snap.TotalItems,
                    bgOpacity    = snap.BgOpacity,
                    fontSize     = snap.FontSize,
                    fontColor    = snap.FontColor,
                    fontOpacity  = snap.FontOpacity,
                    bgColor      = snap.BgColor,
                    columns      = snap.Columns,
                    items        = snap.Items.Select(i => new { n = i.Name, p = i.PickedUp }).ToArray()
                });
                data = Encoding.UTF8.GetBytes(json);
                ct   = "application/json";
            }
            else
            {
                OverlaySnapshot snap;
                lock (_lock) snap = _state;
                data = Encoding.UTF8.GetBytes(BuildHtmlPage(snap));
                ct   = "text/html; charset=utf-8";
            }

            ctx.Response.StatusCode      = 200;
            ctx.Response.ContentType     = ct;
            ctx.Response.ContentLength64 = data.LongLength;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
        }
        catch { }
        finally { try { ctx.Response.OutputStream.Close(); } catch { } }
    }

    public void Dispose()
    {
        _running = false;
        try { _listener.Stop(); } catch { }
    }

    // ── Overlay page ─────────────────────────────────────────────────────────
    private static string HexToRgba(string hex, double alpha)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return $"rgba(13,13,13,{alpha:F3})";
        int r = Convert.ToInt32(hex[..2], 16);
        int g = Convert.ToInt32(hex[2..4], 16);
        int b = Convert.ToInt32(hex[4..6], 16);
        return $"rgba({r},{g},{b},{alpha:F3})";
    }

    private string BuildHtmlPage(OverlaySnapshot snap)
    {
        string bgRgba   = HexToRgba(snap.BgColor,   snap.BgOpacity);
        string fontRgba = HexToRgba(snap.FontColor,  snap.FontOpacity / 100.0);
        int    cols     = Math.Max(1, snap.Columns);
        string gridCols = string.Join(" ", Enumerable.Repeat("1fr", cols));

        return HtmlTemplate
            .Replace("__BG__",       bgRgba)
            .Replace("__FG__",       fontRgba)
            .Replace("__FS__",       snap.FontSize.ToString())
            .Replace("__COLS__",     gridCols)
            .Replace("__BGCOLOR__",  snap.BgColor)
            .Replace("__FGCOLOR__",  snap.FontColor)
            .Replace("__BGOPA__",    snap.BgOpacity.ToString("F3"))
            .Replace("__FGOPA__",    (snap.FontOpacity / 100.0).ToString("F3"))
            .Replace("__COLSN__",    cols.ToString());
    }

    private const string HtmlTemplate = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>DS1 Item Tracker Overlay</title>
        <style>
        *{margin:0;padding:0;box-sizing:border-box;}
        html,body{width:100%;height:100%;background:transparent;}
        body{font-family:'Segoe UI',system-ui,sans-serif;padding:8px;-webkit-font-smoothing:antialiased;}
        .card{
          background:__BG__;
          border-left:3px solid #b8960c;
          border-top:1px solid rgba(184,150,12,0.35);
          border-right:1px solid rgba(184,150,12,0.12);
          border-bottom:1px solid rgba(184,150,12,0.12);
          border-radius:2px;
          padding:10px 14px 12px;
          color:__FG__;
          font-size:__FS__px;
          box-shadow:0 4px 24px rgba(0,0,0,0.55);
          transition:background .2s,font-size .2s,color .2s;
        }
        .hd{display:flex;justify-content:space-between;align-items:center;
          margin-bottom:4px;padding-bottom:6px;border-bottom:1px solid rgba(255,255,255,0.07);}
        .area{color:#d4ac10;font-size:1em;font-weight:700;letter-spacing:.07em;text-transform:uppercase;}
        .prog{opacity:.7;font-size:1em;}
        .tot{display:flex;justify-content:space-between;align-items:center;
          margin-bottom:6px;padding-bottom:6px;border-bottom:1px solid rgba(255,255,255,0.05);font-size:1em;}
        .tot-lbl{opacity:.7;}
        .tot-val{color:#b8960c;font-weight:700;}
        ul{list-style:none;display:grid;grid-template-columns:__COLS__;gap:0 12px;}
        li{display:block;text-align:center;padding:2px 0;font-size:1em;transition:opacity .25s;}
        li.done{opacity:.45;}
        li.done .lbl{text-decoration:line-through;color:#7ec87e;}
        .empty{opacity:.5;font-style:italic;font-size:1em;}
        </style>
        </head>
        <body>
        <div class="card" id="card">
          <div class="tot" id="totrow" style="display:none">
            <span class="tot-lbl">Total:</span>
            <span class="tot-val" id="t"></span>
          </div>
          <div class="hd">
            <span class="area" id="a">Connecting&#x2026;</span>
            <span class="prog" id="p"></span>
          </div>
          <ul id="l"></ul>
        </div>
        <script>
        function esc(s){return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');}
        function hexToRgba(h,a){
          h=h.replace('#','');
          var r=parseInt(h.substring(0,2),16),g=parseInt(h.substring(2,4),16),b=parseInt(h.substring(4,6),16);
          return 'rgba('+r+','+g+','+b+','+a+')';
        }
        (function poll(){
          fetch('/state.json')
            .then(function(r){return r.json();})
            .then(function(s){
              var card=document.getElementById('card');
              card.style.fontSize=(+s.fontSize||11)+'px';
              card.style.background=hexToRgba(s.bgColor||'#0D0D0D',(+s.bgOpacity||0.55).toFixed(3));
              card.style.color=hexToRgba(s.fontColor||'#E0D5C0',(+s.fontOpacity||1).toFixed(3));
              var cols=Math.max(1,+s.columns||1);
              document.getElementById('l').style.gridTemplateColumns=Array(cols).fill('1fr').join(' ');
              document.getElementById('a').textContent=s.areaName||'\u2014';
              document.getElementById('p').textContent=s.total>0?s.pickedUp+' / '+s.total:'';
              var totRow=document.getElementById('totrow');
              if(s.totalItems>0){
                document.getElementById('t').textContent=s.totalPickedUp+' / '+s.totalItems;
                totRow.style.display='flex';
              }else{totRow.style.display='none';}
              var ul=document.getElementById('l');
              if(!s.items||!s.items.length){
                ul.innerHTML='<li><span class="empty">No items</span></li>';
              }else{
                ul.innerHTML=s.items.map(function(i){
                  return '<li class="'+(i.p?'done':'todo')+'">'
                    +'<span class="lbl">'+esc(i.n)+'</span></li>';
                }).join('');
              }
            })
            .catch(function(){
              document.getElementById('a').textContent='Not connected';
              document.getElementById('p').textContent='';
              document.getElementById('l').innerHTML='';
            })
            .finally(function(){setTimeout(poll,500);});
        })();
        </script>
        </body>
        </html>
        """;
}