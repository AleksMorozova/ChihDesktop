using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: WalkAlignment <original-backup-directory> <sprite-directory>");
        string backup = Path.GetFullPath(args[0]), destination = Path.GetFullPath(args[1]);
        // Measured dark-torso centroids: 142.44,145.54,143.35,143.97; target ~143.5.
        // Integer translations only; no resampling, recoloring, scaling or new artwork.
        int[] shifts = [1, -2, 0, 0];
        var before = new List<string>();
        var after = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            string name = $"WalkRight-{i:00}.png";
            byte[] original = File.ReadAllBytes(Path.Combine(backup, name));
            before.Add(Convert.ToBase64String(original));
            using var stream = new MemoryStream(original);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            if (bitmap.PixelWidth != 320 || bitmap.PixelHeight != 260) throw new InvalidDataException(name);
            byte[] pixels = new byte[320 * 260 * 4], translated = new byte[320 * 260 * 4];
            bitmap.CopyPixels(pixels, 1280, 0);
            for (int y = 0; y < 260; y++)
            for (int x = 0; x < 320; x++)
            {
                int targetX = x + shifts[i], source = (y * 320 + x) * 4;
                if (targetX < 0 || targetX >= 320)
                {
                    if (pixels[source + 3] != 0) throw new InvalidDataException($"Translation would clip {name}");
                    continue;
                }
                Buffer.BlockCopy(pixels, source, translated, (y * 320 + targetX) * 4, 4);
            }
            string output = Path.Combine(destination, name);
            if (shifts[i] != 0)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(320, 260, 96, 96, PixelFormats.Bgra32, null, translated, 1280)));
                using var file = File.Create(output);
                encoder.Save(file);
            }
            else if (!File.ReadAllBytes(output).SequenceEqual(original))
                throw new InvalidDataException($"Unshifted source differs from backup: {name}");
            using var checkStream = File.OpenRead(output);
            var checkDecoder = new PngBitmapDecoder(checkStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var check = new FormatConvertedBitmap(checkDecoder.Frames[0], PixelFormats.Bgra32, null, 0);
            byte[] actual = new byte[pixels.Length];
            check.CopyPixels(actual, 1280, 0);
            if (!actual.SequenceEqual(translated)) throw new InvalidDataException($"Pixel integrity failed: {name}");
            int bottom = -1;
            for (int p = 3; p < actual.Length; p += 4)
                if (actual[p] > 32) bottom = p / 4 / 320;
            if (bottom != 244) throw new InvalidDataException($"Ground baseline changed: {name}");
            after.Add(Convert.ToBase64String(File.ReadAllBytes(output)));
            Console.WriteLine($"{name}: dx={shifts[i]}, dy=0, baseline={bottom}; exact translated RGBA verified; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output)))}");
        }
        string html = """
<!doctype html><meta charset="utf-8"><title>Chih walk alignment comparison</title>
<style>body{background:#20252c;color:#eee;font:16px system-ui;margin:30px}main{display:flex;gap:24px}figure{margin:0}canvas{background:#52786e;max-width:100%}button{margin:20px 10px 20px 0;padding:8px}p{max-width:850px}</style>
<h1>Walk: before / after</h1><p>Original artwork at 1:1 source resolution. Pink line: y=244 ground baseline. Blue line: torso target x=143.5. Canvas translations only: +1, −2, 0, 0 pixels horizontally. No vertical shift, scaling or new frames.</p>
<main><figure><figcaption>Before</figcaption><canvas id="before" width="320" height="260"></canvas></figure><figure><figcaption>After</figcaption><canvas id="after" width="320" height="260"></canvas></figure></main>
<button id="toggle">Pause</button><button id="step">Next frame</button><span id="frame"></span>
<p>The remaining changes in back shape, head position and leg proportions are in the source poses. They cannot be removed by moving the transparent canvas. This preview isolates sprite registration; it does not simulate native window scheduling.</p>
<script>
""";
        html += $"const beforeData={System.Text.Json.JsonSerializer.Serialize(before)},afterData={System.Text.Json.JsonSerializer.Serialize(after)};\n";
        html += """
const load=data=>Promise.all(data.map(s=>new Promise(resolve=>{let i=new Image();i.onload=()=>resolve(i);i.src='data:image/png;base64,'+s})));
Promise.all([load(beforeData),load(afterData)]).then(([beforeImages,afterImages])=>{
let paused=false,frame=0,last=-1,start=performance.now();
function draw(){for(const [id,images] of [['before',beforeImages],['after',afterImages]]){const c=document.getElementById(id).getContext('2d');c.clearRect(0,0,320,260);c.drawImage(images[frame],0,0);c.strokeStyle='#ef91b0';c.beginPath();c.moveTo(0,244.5);c.lineTo(320,244.5);c.stroke();c.strokeStyle='#71d0ff88';c.beginPath();c.moveTo(143.5,0);c.lineTo(143.5,260);c.stroke()}document.getElementById('frame').textContent='Frame '+frame}
document.getElementById('toggle').onclick=()=>{paused=!paused;start=performance.now()-frame*125;document.getElementById('toggle').textContent=paused?'Play':'Pause'};
document.getElementById('step').onclick=()=>{paused=true;document.getElementById('toggle').textContent='Play';frame=(frame+1)%4;draw()};
function tick(t){if(!paused){frame=Math.floor((t-start)/125)%4;if(frame!==last){draw();last=frame}}requestAnimationFrame(tick)}draw();requestAnimationFrame(tick);
});</script>
""";
        File.WriteAllText(Path.Combine(backup, "before-after.html"), html, Encoding.UTF8);
    }
}
