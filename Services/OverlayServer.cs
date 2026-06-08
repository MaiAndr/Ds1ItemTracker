namespace Ds1ItemTracker.Services;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Linq;

public sealed record OverlayItem(string Name, bool PickedUp);

public sealed class OverlaySnapshot
{
    public string AreaName  { get; init; } = "—";
    public int    PickedUp  { get; init; }
    public int    Total     { get; init; }
    public double BgOpacity { get; init; } = 0.55;
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
                    areaName  = snap.AreaName,
                    pickedUp  = snap.PickedUp,
                    total     = snap.Total,
                    bgOpacity = snap.BgOpacity,
                    items     = snap.Items.Select(i => new { n = i.Name, p = i.PickedUp }).ToArray()
                });
                data = Encoding.UTF8.GetBytes(json);
                ct   = "application/json";
            }
            else
            {
                data = Encoding.UTF8.GetBytes(HtmlPage);
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
    private const string HtmlPage = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>DS1 Item Tracker Overlay</title>
        <style>
        *{margin:0;padding:0;box-sizing:border-box;}
        html,body{width:100%;height:100%;background:transparent;}
        body{
          font-family:'Segoe UI',system-ui,sans-serif;
          color:#e0d5c0;
          padding:8px;
          -webkit-font-smoothing:antialiased;
        }
        :root{--bg-alpha:0.55;}
        .card{
          background:rgba(13,13,13,var(--bg-alpha));
          border-left:3px solid #b8960c;
          border-top:1px solid rgba(184,150,12,0.35);
          border-right:1px solid rgba(184,150,12,0.12);
          border-bottom:1px solid rgba(184,150,12,0.12);
          border-radius:2px;
          padding:10px 14px 12px;
          width:300px;
          box-shadow:0 4px 24px rgba(0,0,0,0.55);
          transition:background .2s,font-size .2s;
        }
        .hd{
          display:flex;justify-content:space-between;align-items:center;
          margin-bottom:8px;padding-bottom:6px;
          border-bottom:1px solid rgba(255,255,255,0.07);
        }
        .area{color:#d4ac10;font-size:11px;font-weight:700;letter-spacing:.07em;text-transform:uppercase;}
        .prog{color:#888070;font-size:11px;}
        ul{list-style:none;}
        li{display:flex;align-items:center;gap:7px;padding:2px 0;font-size:12px;transition:opacity .25s;}
        li.done{opacity:.45;}
        li.done .lbl{text-decoration:line-through;color:#7ec87e;}
        li.todo .lbl{color:#c8b898;}
        .ico{width:12px;font-size:10px;flex-shrink:0;text-align:center;}
        li.done .ico{color:#7ec87e;}
        li.todo .ico{color:#505050;}
        .empty{color:#606060;font-style:italic;font-size:11px;}
        </style>
        </head>
        <body>
        <div class="card" id="card">
          <div class="hd">
            <span class="area" id="a">Connecting…</span>
            <span class="prog" id="p"></span>
          </div>
          <ul id="l"></ul>
        </div>
        <script>
        function esc(s){return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');}
        (function poll(){
          fetch('/state.json')
            .then(function(r){return r.json();})
            .then(function(s){
              /* Apply settings pushed from the app */
              document.documentElement.style.setProperty('--bg-alpha',(+s.bgOpacity||0.55).toFixed(3));

              document.getElementById('a').textContent=s.areaName||'\u2014';
              document.getElementById('p').textContent=s.total>0?s.pickedUp+' / '+s.total:'';
              var ul=document.getElementById('l');
              if(!s.items||!s.items.length){
                ul.innerHTML='<li><span class="empty">No items</span></li>';
              }else{
                ul.innerHTML=s.items.map(function(i){
                  return '<li class="'+(i.p?'done':'todo')+'">'+
                    '<span class="ico">'+(i.p?'&#10003;':'&#9675;')+'</span>'+
                    '<span class="lbl">'+esc(i.n)+'</span></li>';
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
