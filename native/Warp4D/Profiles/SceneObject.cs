using System.Drawing;
using System.Drawing.Imaging;
using Warp4D.Rendering;

namespace Warp4D.Profiles;

internal enum SceneObjectKind
{
    Bush,
    Cloud,
    Hill,
    Tree,
    Pipe,
    QuestionBlock,
    Brick,
    Terrain,
    Castle,
    Flagpole,
    Player,
    Enemy,
    Item,
    Sprite
}

internal sealed class SceneObject : IDisposable
{
    private PixelGeometry? _pixelGeometry;
    private ImagePixels? _pixels;

    public required SceneObjectKind Kind { get; init; }
    public required string Label { get; init; }
    public string IdentityKey { get; init; } = string.Empty;
    public string PresentationKey => string.IsNullOrEmpty(IdentityKey) ? $"{Kind}:{Label}" : IdentityKey;
    public required Rectangle Bounds { get; init; }
    public required Bitmap Image { get; init; }
    public required Color Accent { get; init; }
    public required float Depth { get; init; }
    public required bool ProjectionEnabled { get; init; }
    public int SortOrder { get; init; }
    public ImagePixels Pixels => _pixels ??= ImagePixels.Read(Image);
    public PixelGeometry PixelGeometry => _pixelGeometry ??= GeometryCache.Get(Pixels, Image);

    public void Dispose() => Image.Dispose();
}

internal readonly record struct PixelRun(int X, int Y, int Length, Color Color);

internal readonly record struct PixelBoundary(PointF Start, PointF End, Color Color);

internal sealed class PixelGeometry
{
    private sealed record BoundaryTopology(PointF[] Points,(int Start,int End)[] Edges,Color[] Colors);
    private sealed record LocalTopology(int ImageWidth,int ImageHeight,float HalfX,float HalfY,PointF[] Points);
    private volatile LocalTopology? _localTopology;
    private volatile BoundaryTopology? _topology;
    internal (PointF[] Points,(int Start,int End)[] Edges,Color[] Colors) Topology
    {
        get
        {
            if(_topology is {} cached)return (cached.Points,cached.Edges,cached.Colors);
            Dictionary<PointF,int> indices=[];List<PointF> points=[];
            int Index(PointF point){if(indices.TryGetValue(point,out int i))return i;indices[point]=points.Count;points.Add(point);return points.Count-1;}
            (int Start,int End)[] edges=new (int,int)[Boundary.Count];
            for(int i=0;i<edges.Length;i++)edges[i]=(Index(Boundary[i].Start),Index(Boundary[i].End));
            var built=new BoundaryTopology(points.ToArray(),edges,Boundary.Select(edge=>edge.Color).ToArray());_topology=built;return (built.Points,built.Edges,built.Colors);
        }
    }
    internal PointF[] LocalPoints(int imageWidth,int imageHeight,float halfX,float halfY)
    {
        if(_localTopology is {} cached && cached.ImageWidth==imageWidth && cached.ImageHeight==imageHeight && cached.HalfX==halfX && cached.HalfY==halfY)return cached.Points;
        var points=Topology.Points;
        PointF[] local=new PointF[points.Length];
        for(int i=0;i<local.Length;i++)local[i]=new(-halfX+(points[i].X/imageWidth)*halfX*2f,-halfY+(points[i].Y/imageHeight)*halfY*2f);
        _localTopology=new(imageWidth,imageHeight,halfX,halfY,local);
        return local;
    }
    public required IReadOnlyList<PixelRun> Runs { get; init; }
    public required IReadOnlyList<PixelBoundary> Boundary { get; init; }

    public static unsafe PixelGeometry FromBitmap(Bitmap bitmap)
    {
        List<PixelRun> runs = [];
        List<PixelBoundary> boundary = [];
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                int* row = (int*)((byte*)data.Scan0 + y * data.Stride);
                int x = 0;
                while (x < bitmap.Width)
                {
                    int argb = row[x];
                    if ((argb & unchecked((int)0xFF000000)) == 0)
                    {
                        x++;
                        continue;
                    }

                    int start = x;
                    while (x + 1 < bitmap.Width && row[x + 1] == argb)
                    {
                        x++;
                    }
                    runs.Add(new PixelRun(start, y, x - start + 1, Color.FromArgb(argb)));
                    x++;
                }
            }

            // Merge adjacent exposed pixel edges into longer same-color segments.
            // The resulting side wall is geometrically identical but needs far
            // fewer projected polygons than one boundary quad per NES pixel.
            for (int y = 0; y < bitmap.Height; y++)
            {
                int* row = (int*)((byte*)data.Scan0 + y * data.Stride);
                int x = 0;
                while (x < bitmap.Width)
                {
                    int argb = row[x];
                    if (!Visible(argb) || Opaque(x, y - 1))
                    {
                        x++;
                        continue;
                    }
                    int start = x++;
                    while (x < bitmap.Width && row[x] == argb && !Opaque(x, y - 1)) x++;
                    boundary.Add(new PixelBoundary(new PointF(start, y), new PointF(x, y), Color.FromArgb(argb)));
                }

                x = 0;
                while (x < bitmap.Width)
                {
                    int argb = row[x];
                    if (!Visible(argb) || Opaque(x, y + 1))
                    {
                        x++;
                        continue;
                    }
                    int start = x++;
                    while (x < bitmap.Width && row[x] == argb && !Opaque(x, y + 1)) x++;
                    boundary.Add(new PixelBoundary(new PointF(x, y + 1), new PointF(start, y + 1), Color.FromArgb(argb)));
                }
            }

            for (int x = 0; x < bitmap.Width; x++)
            {
                int y = 0;
                while (y < bitmap.Height)
                {
                    int argb = PixelAt(x, y);
                    if (!Visible(argb) || Opaque(x + 1, y))
                    {
                        y++;
                        continue;
                    }
                    int start = y++;
                    while (y < bitmap.Height && PixelAt(x, y) == argb && !Opaque(x + 1, y)) y++;
                    boundary.Add(new PixelBoundary(new PointF(x + 1, start), new PointF(x + 1, y), Color.FromArgb(argb)));
                }

                y = 0;
                while (y < bitmap.Height)
                {
                    int argb = PixelAt(x, y);
                    if (!Visible(argb) || Opaque(x - 1, y))
                    {
                        y++;
                        continue;
                    }
                    int start = y++;
                    while (y < bitmap.Height && PixelAt(x, y) == argb && !Opaque(x - 1, y)) y++;
                    boundary.Add(new PixelBoundary(new PointF(x, y), new PointF(x, start), Color.FromArgb(argb)));
                }
            }

            static bool Visible(int argb) =>
                (argb & unchecked((int)0xFF000000)) != 0;

            int PixelAt(int x, int y)
            {
                int* checkRow = (int*)((byte*)data.Scan0 + y * data.Stride);
                return checkRow[x];
            }

            bool Opaque(int x, int y)
            {
                if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
                {
                    return false;
                }
                return Visible(PixelAt(x, y));
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return new PixelGeometry { Runs = runs, Boundary = boundary };
    }
}

internal sealed class SmbScene : IDisposable
{
    public required Bitmap Background { get; init; }
    public required IReadOnlyList<SceneObject> Objects { get; init; }
    public required string Location { get; init; }
    public required bool ExactProfile { get; init; }
    public IReadOnlyList<Rectangle> FlatRegions { get; init; } = [];
    public required string RecognitionProfileName { get; init; }
    public required string ProjectionProfileName { get; init; }
    public long Sequence { get; init; }
    public bool PlayerOverlaysFlatHud { get; init; }

    public SmbScene Clone() => new()
    {
        Background = new Bitmap(Background),
        Objects = Objects.Select(item => new SceneObject
        {
            Kind = item.Kind, Label = item.Label, IdentityKey = item.IdentityKey,
            Bounds = item.Bounds, Image = new Bitmap(item.Image), Accent = item.Accent,
            Depth = item.Depth, ProjectionEnabled = item.ProjectionEnabled, SortOrder = item.SortOrder
        }).ToArray(),
        Location = Location, ExactProfile = ExactProfile, FlatRegions = FlatRegions.ToArray(), RecognitionProfileName = RecognitionProfileName,
        ProjectionProfileName = ProjectionProfileName, Sequence = Sequence, PlayerOverlaysFlatHud = PlayerOverlaysFlatHud
    };

    public void Dispose()
    {
        Background.Dispose();
        foreach (SceneObject item in Objects)
        {
            item.Dispose();
        }
    }
}
