using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D.UI;

internal sealed class DimensionStudioForm : Form
{
    private readonly WarpRendererControl _renderer;
    private readonly Func<PresentationSettings> _current;
    private readonly Action<PresentationSettings> _apply;
    private readonly Action<PresentationSettings,PresentationSettings,float> _blend;
    private readonly Action _record,_recognition;
    private readonly TabControl _tabs=new(){Dock=DockStyle.Fill};
    private readonly System.Windows.Forms.Timer _timer=new(){Interval=50};
    private readonly System.Diagnostics.Stopwatch _clock=new();
    private readonly CheckBox _auto=new(){Text="Automatically crossfade",AutoSize=true};
    private readonly TrackBar _fader=new(){Minimum=0,Maximum=1000,TickStyle=TickStyle.None,Width=360};
    private readonly ComboBox _kind=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=280};
    private readonly ComboBox _motion=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=280};
    private readonly Label _picked=new(){AutoSize=true,MaximumSize=new(370,90)};
    private readonly Label _audio=new(){AutoSize=true,MaximumSize=new(370,80)};
    private readonly Label _recordStatus=new(){AutoSize=true,MaximumSize=new(370,80)};
    private readonly Label _aLabel=new(){AutoSize=true},_bLabel=new(){AutoSize=true};
    private readonly TrackBar _strength=new(){Minimum=0,Maximum=100,TickStyle=TickStyle.None,Width=360};
    private readonly TrackBar _speed=new(){Minimum=0,Maximum=200,TickStyle=TickStyle.None,Width=360};
    private PresentationSettings _a,_b;
    private string? _objectKey;
    private SceneObjectKind? _objectKind;
    private bool _sync;
    private double _blendSpeed=.1;
    public Func<string>? AudioStatus {get;set;}
    public Func<string>? RecordStatus {get;set;}
    public DimensionStudioForm(WarpRendererControl renderer,Func<PresentationSettings> current,Action<PresentationSettings> apply,
        Action<PresentationSettings,PresentationSettings,float> blend,Action record,Action recognition)
    {
        _renderer=renderer;_current=current;_apply=apply;_blend=blend;_record=record;_recognition=recognition;
        _a=current().Clone();_b=LookCatalog.Create(6,current());
        NativeTheme.Apply(this);Text="Warp4D · Dimension Studio";ClientSize=new(430,680);MinimumSize=new(440,540);StartPosition=FormStartPosition.CenterParent;
        Controls.Add(_tabs);NativeTheme.StyleTabs(_tabs);_tabs.ItemSize=new(95,30);NativeTheme.StyleComboBox(_kind);NativeTheme.StyleComboBox(_motion);
        BuildMotion();BuildPaint();BuildBlend();BuildFinish();
        _timer.Tick+=(_,_)=>
        {
            if(_auto.Checked&&InterfaceMotion.Enabled){_fader.Value=(int)((Math.Sin(_clock.Elapsed.TotalSeconds*Math.Tau*_blendSpeed)+1)*500);}
            _audio.Text=(AudioStatus?.Invoke()??"Audio session meter")+"\nEnvelope: "+_renderer.AudioLevel.ToString("0.00");
            _recordStatus.Text=RecordStatus?.Invoke()??"Not recording";
        };_timer.Start();
        FormClosed+=(_,_)=>{_renderer.PaintObjects=false;_renderer.SelectedObjectKey=null;_renderer.Invalidate();};
        SyncMotion();
    }
    private FlowLayoutPanel Page(string title)
    {
        TabPage page=new(title){BackColor=Color.FromArgb(12,18,27),ForeColor=Color.White};
        FlowLayoutPanel stack=new(){Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new(14),BackColor=page.BackColor};
        page.Controls.Add(stack);_tabs.TabPages.Add(page);return stack;
    }
    private static void TextRow(FlowLayoutPanel stack,string text)
    {stack.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new(370,150),Margin=new(0,8,0,9)});}
    private static Button ButtonRow(FlowLayoutPanel stack,string text,Action click)
    {
        Button button=new(){Text=text,Width=360,Height=34,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(23,35,46),ForeColor=Color.FromArgb(173,255,93),Margin=new(0,4,0,4)};
        button.Click+=(_,_)=>click();stack.Controls.Add(button);return button;
    }
    private CheckBox Toggle(FlowLayoutPanel stack,string text,bool initial,Action<DimensionSettings,bool> setter)
    {
        CheckBox check=new(){Text=text,Checked=initial,AutoSize=true,Margin=new(0,10,0,8)};
        check.CheckedChanged+=(_,_)=>Edit(s=>setter(s.Dimensions,check.Checked));stack.Controls.Add(check);return check;
    }
    private static TrackBar Slider(FlowLayoutPanel stack,string text,int low,int high,int value,Action<int> changed)
    {
        Label label=new(){Text=text+"  "+value,AutoSize=true,Margin=new(0,8,0,0)};stack.Controls.Add(label);
        TrackBar slider=new(){Width=360,Minimum=low,Maximum=high,Value=value,TickStyle=TickStyle.None,Margin=new(0,0,0,0)};
        slider.ValueChanged+=(_,_)=>{label.Text=text+"  "+slider.Value;changed(slider.Value);};stack.Controls.Add(slider);return slider;
    }
    private void Edit(Action<PresentationSettings> action)
    {if(_sync)return;StopAutoBlend();var s=_current().Clone();action(s);s.Normalize();_apply(s);}
    private void BuildMotion()
    {
        var page=Page("Motion");TextRow(page,"Independent motion personalities for recognized object classes. Manual opacity and layer edits stay unchanged.");
        Toggle(page,"Enable object choreography",_current().Dimensions.Choreography,(d,v)=>d.Choreography=v);
        foreach(var kind in Enum.GetValues<SceneObjectKind>())_kind.Items.Add(kind);_kind.SelectedIndex=0;
        page.Controls.Add(_kind);_kind.SelectedIndexChanged+=(_,_)=>SyncMotion();
        _motion.Items.AddRange(Enum.GetNames<MotionStyle>());page.Controls.Add(_motion);_motion.SelectedIndexChanged+=(_,_)=>SaveMotion();
        TextRow(page,"Motion strength (0–100)");page.Controls.Add(_strength);_strength.ValueChanged+=(_,_)=>SaveMotion();
        TextRow(page,"Motion speed (hundredths of a cycle/second)");page.Controls.Add(_speed);_speed.ValueChanged+=(_,_)=>SaveMotion();
        ButtonRow(page,"APPLY THIS MOTION TO PICKED OBJECT",()=>
        {
            if(_objectKey is null){MessageBox.Show(this,"Use Paint and click an object first.");return;}
            Edit(s=>s.Dimensions.ObjectMotions[_objectKey]=new(){Style=(MotionStyle)_motion.SelectedIndex,Strength=_strength.Value/100f,Speed=_speed.Value/100d});
        });
        TextRow(page,"4D orientation · drag any of the six planes");page.Controls.Add(new DimensionCompass(_renderer){Width=360});
    }
    private void SyncMotion()
    {
        if(_kind.SelectedItem is not SceneObjectKind kind)return;_sync=true;
        var motion=_current().Dimensions.ClassMotions.GetValueOrDefault(kind.ToString())??new();
        _motion.SelectedIndex=(int)motion.Style;_strength.Value=(int)(motion.Strength*100);_speed.Value=(int)(motion.Speed*100);_sync=false;
    }
    private void SaveMotion()
    {
        if(_sync||_kind.SelectedItem is not SceneObjectKind kind||_motion.SelectedIndex<0)return;
        Edit(s=>s.Dimensions.ClassMotions[kind.ToString()]=new(){Style=(MotionStyle)_motion.SelectedIndex,Strength=_strength.Value/100f,Speed=_speed.Value/100d});
    }
    private void BuildPaint()
    {
        var page=Page("Paint");TextRow(page,"Turn on painting, then click an object in the running game. Choose an effect below to preview immediately. Matching means the recognized object class; it does not guess new tile patterns.");
        CheckBox paint=new(){Text="Paint objects with left click",AutoSize=true,Checked=_renderer.PaintObjects};paint.CheckedChanged+=(_,_)=>_renderer.PaintObjects=paint.Checked;page.Controls.Add(paint);
        _picked.Text="No object picked yet.";page.Controls.Add(_picked);
        ButtonRow(page,"ENABLE PICKED CLASS'S PROJECTION",()=>
        {if(_objectKind is not null)Edit(s=>s.ProjectionProfile.RuleFor(_objectKind.Value).Enabled=true);});
        foreach(var mode in Enum.GetValues<GeometryMode>())
        {
            var selected=mode;ButtonRow(page,"PAINT · "+GeometryCatalog.Names[(int)mode],()=>
            {
                if(_objectKey is null){MessageBox.Show(this,"Click a recognized game object first.");return;}
                Edit(s=>s.ObjectGeometries[_objectKey]=new(){Mode=selected,Phase=.5f});
            });
        }
        ButtonRow(page,"APPLY PICKED LOOK TO MATCHING CLASS",()=>
        {
            if(_objectKey is null||_objectKind is null)return;
            Edit(s=>{s.ClassGeometries[_objectKind.ToString()!]=(s.ObjectGeometries.GetValueOrDefault(_objectKey)??s.Geometry).Clone();s.ObjectGeometries.Remove(_objectKey);});
        });
        ButtonRow(page,"CLEAR PICKED / CLASS LOOK OVERRIDES",()=>
        {if(_objectKey is not null&&_objectKind is not null)Edit(s=>{s.ObjectGeometries.Remove(_objectKey);s.ClassGeometries.Remove(_objectKind.ToString()!);s.Dimensions.ObjectMotions.Remove(_objectKey);});});
        Slider(page,"Slice picked object / global W plane",0,100,50,v=>Edit(s=>
        {var geometry=SliceGeometry(s);geometry.Phase=v/100f;geometry.Animate=false;}));
        CheckBox sweep=new(){Text="Sweep slicing plane automatically",AutoSize=true};
        sweep.CheckedChanged+=(_,_)=>Edit(s=>{var geometry=SliceGeometry(s);geometry.Animate=sweep.Checked;geometry.Speed=.12;if(sweep.Checked)s.Animate=true;});page.Controls.Add(sweep);
        ButtonRow(page,"EDIT GAME RECOGNITION / TILE PATTERNS",()=>_recognition());
    }
    public void SelectObject(SceneObject item)
    {
        _objectKey=item.PresentationKey;_objectKind=item.Kind;_picked.Text=item.Label+"\n"+item.PresentationKey+"\nClass: "+item.Kind+(item.ProjectionEnabled?"":" · 2D disabled by profile");
        _renderer.SelectedObjectKey=_objectKey;_kind.SelectedItem=item.Kind;_tabs.SelectedIndex=1;
    }
    private GeometrySettings SliceGeometry(PresentationSettings settings)
    {
        if(_objectKey is null){settings.BlendSource=null;settings.Geometry.Mode=GeometryMode.Slicing;return settings.Geometry;}
        if(!settings.ObjectGeometries.TryGetValue(_objectKey,out var geometry))settings.ObjectGeometries[_objectKey]=geometry=new();
        geometry.Mode=GeometryMode.Slicing;return geometry;
    }
    private void BuildBlend()
    {
        var page=Page("Blend");TextRow(page,"Blend actual 4D surfaces between A and B. Rotations, depth, camera and finish interpolate. Opacity, recognition rules and individual object edits stay yours. Slicing blends use the sheet/slab preview until you commit the endpoint.");
        _aLabel.Text="A · "+_a.Name;page.Controls.Add(_aLabel);
        ButtonRow(page,"LOAD SAVED LOOK A",()=>LoadLook(true));ButtonRow(page,"CAPTURE CURRENT AS A",()=>{StopAutoBlend();_a=_current().Clone();_aLabel.Text="A · "+_a.Name;});
        _bLabel.Text="B · "+_b.Name;page.Controls.Add(_bLabel);
        ButtonRow(page,"LOAD SAVED LOOK B",()=>LoadLook(false));ButtonRow(page,"CAPTURE CURRENT AS B",()=>{StopAutoBlend();_b=_current().Clone();_bLabel.Text="B · "+_b.Name;});
        TextRow(page,"A  ←  CROSSFADE  →  B");page.Controls.Add(_fader);_fader.ValueChanged+=(_,_)=>_blend(_a,_b,_fader.Value/1000f);
        page.Controls.Add(_auto);_auto.CheckedChanged+=(_,_)=>{if(_auto.Checked)_clock.Restart();else _clock.Stop();};
        Slider(page,"Crossfade speed (hundredths Hz)",1,100,10,v=>_blendSpeed=v/100d);
        ButtonRow(page,"SAVE CURRENT BLEND AS LOOK",()=>
        {
            using SaveFileDialog dialog=new(){Filter="Warp4D look|*.warp4d-look.json",FileName="Dimensional blend.warp4d-look.json"};
            if(dialog.ShowDialog(this)==DialogResult.OK){StopAutoBlend();try{PresentationSettingsStore.WriteToFile(dialog.FileName,_current());}catch(Exception e)when(e is IOException or UnauthorizedAccessException){MessageBox.Show(this,e.Message);}}
        });
        TextRow(page,"Saved looks retain the source geometry and blend amount, so intermediate blends can be reopened. Existing per-object overrides remain independent.");
    }
    private void LoadLook(bool a)
    {
        using OpenFileDialog dialog=new(){Filter="Warp4D look|*.json"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{var look=PresentationSettingsStore.ReadFromFile(dialog.FileName);StopAutoBlend();if(a){_a=look;_aLabel.Text="A · "+look.Name;}else{_b=look;_bLabel.Text="B · "+look.Name;}}
        catch(Exception e)when(e is IOException or System.Text.Json.JsonException or InvalidDataException){MessageBox.Show(this,e.Message,"Could not load look");}
    }
    private void BuildFinish()
    {
        var page=Page("Finish");var d=_current().Dimensions;
        Toggle(page,"Dimension trails (current sprite, past rotations)",d.Trails,(s,v)=>s.Trails=v);
        Slider(page,"Trail length",1,6,d.TrailLength,v=>Edit(s=>s.Dimensions.TrailLength=v));
        Slider(page,"Trail opacity",0,100,(int)(d.TrailOpacity*100),v=>Edit(s=>s.Dimensions.TrailOpacity=v/100f));
        Toggle(page,"Music / audio-reactive motion",d.AudioReactive,(s,v)=>s.AudioReactive=v);
        Slider(page,"Audio response",0,100,(int)(d.AudioStrength*100),v=>Edit(s=>s.Dimensions.AudioStrength=v/100f));page.Controls.Add(_audio);
        TextRow(page,"Responds to Warp4D's own audio amplitude, including game sound effects. Not beat detection. Silence stays neutral; reduced motion stops animated effects.");
        Toggle(page,"Performance-aware cosmetic detail",d.AdaptiveQuality,(s,v)=>s.AdaptiveQuality=v);
        Slider(page,"Target frame rate",20,120,d.TargetFps,v=>Edit(s=>s.Dimensions.TargetFps=v));
        TextRow(page,"Reduces tessellation, glow/trails and GPU supersampling when drawing exceeds budget. Recognition, input and emulation speed are unchanged. A target is not a guaranteed frame rate.");
        ButtonRow(page,"START / STOP CLEAN VIDEO (CTRL+F12)",()=>_record());page.Controls.Add(_recordStatus);
        TextRow(page,"512 × 480, 30 fps MJPEG AVI with native game audio. Menus, labels and compass are excluded. Recording stops before ROM changes or pause. Hold F9 for original NES pixels; release to return to 4D.");
    }
    public void StopAutoBlend(){_auto.Checked=false;_clock.Stop();}
    internal void SelectTabForTest(int index)=>_tabs.SelectedIndex=index;
    internal void SetFaderForTest(int value)=>_fader.Value=value;
    internal void SetAutoForTest(bool value)=>_auto.Checked=value;
    protected override void Dispose(bool disposing){if(disposing)_timer.Dispose();base.Dispose(disposing);}
}
