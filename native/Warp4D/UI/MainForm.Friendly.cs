using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D.UI;

internal sealed partial class MainForm
{
    private readonly ToolTip _friendlyTips=new(){InitialDelay=400,AutoPopDelay=15000};
    private readonly ContextMenuStrip _moreMenu=new();
    private readonly TabControl _friendlyTabs=new FriendlyTabs(){Dock=DockStyle.Fill};
    private readonly System.Windows.Forms.Timer _friendlyTimer=new(){Interval=100};
    private readonly List<(CheckBox Check,Func<bool> Read)> _friendlyChecks=[];
    private readonly List<(TrackBar Slider,Func<int> Read)> _friendlySliders=[];
    private readonly Label _friendlyProfile=MakeLabel("Open a game to detect its objects.",9,MutedColor);
    private readonly Label _friendlyPicked=MakeLabel("No object selected",10,TextColor,FontStyle.Bold);
    private readonly Label _friendlyPickHelp=MakeLabel("Click Select object, then click artwork in the game.",9,MutedColor);
    private readonly Label _friendlyRecording=MakeLabel("Not recording",9,MutedColor);
    private readonly Label _friendlySaveNotice=MakeLabel("Changes save automatically for each game.",9,MutedColor);
    private readonly Label _friendlyHelp=MakeLabel("Choose a style. Make it yours.",9,MutedColor);
    private readonly Label _friendlyLookName=MakeLabel("Custom look",11,TextColor,FontStyle.Bold);
    private readonly List<Control> _requiresGame=[];
    private readonly List<Control> _requiresSelection=[];
    private Button? _friendlyUndoButton,_friendlyRecordButton,_friendlySelectButton;
    private CheckBox? _friendlyClassScope,_friendlyBlendAuto;
    private TrackBar? _friendlyFader;
    private Label? _friendlyBlendAName,_friendlyBlendBName;
    private PresentationSettings? _friendlyUndo,_friendlyBlendA,_friendlyBlendB;
    private string? _friendlyObjectKey,_friendlyLayerKey;
    private SceneObjectKind? _friendlyObjectKind;
    private bool _friendlySync,_friendlyGesture;
    private readonly System.Diagnostics.Stopwatch _friendlyBlendClock=new();
    private readonly List<Button> _friendlyGeometryButtons=[];
    internal int FriendlyPagesForTest=>_friendlyTabs.TabPages.Count;
    internal void FriendlyPageForTest(int index)=>ShowFriendlyPage(index);
    internal void FriendlySetKnobForTest(string title,int value)=>_friendlySliders.Single(s=>s.Slider.AccessibleName==title).Slider.Value=value;
    internal void FriendlyFocusKnobForTest(string title)=>_friendlySliders.Single(s=>s.Slider.AccessibleName==title).Slider.Focus();
    internal void FriendlyUndoForTest()=>UndoFriendly();
    internal void FriendlyResetForTest()=>ResetFriendlyLook();
    internal void FriendlyObjectForTest()=>PickFriendlyObject(_renderer.SceneObjects.First(i=>i.ProjectionEnabled&&i.Kind is SceneObjectKind.Player or SceneObjectKind.Enemy or SceneObjectKind.Sprite).PresentationKey,"Center");
    internal void FriendlyShapeForTest(GeometryMode mode)=>_friendlyGeometryButtons[(int)mode].PerformClick();
    internal bool FriendlyGameActionsDisabledForTest=>_requiresGame.All(c=>!c.Enabled);
    internal bool FriendlySelectionActionsEnabledForTest=>_requiresSelection.All(c=>c.Enabled);
    internal GeometryMode? FriendlyObjectShapeForTest=>FriendlySelectedGeometry()?.Mode;
    internal string? FriendlySelectedKeyForTest=>_friendlyObjectKey;
    internal void FriendlyScopeForTest(bool whole)=>_friendlyClassScope!.Checked=whole;
    internal void FriendlyBlendForTest(int value)=>_friendlyFader!.Value=value;
    internal void FriendlyAutoBlendForTest(bool value)=>_friendlyBlendAuto!.Checked=value;
    internal void FriendlyToggleMotionForTest()=>_cycleCheckBox!.Checked=!_cycleCheckBox.Checked;
    internal void FriendlySetMotionForTest(string title,bool value)=>_friendlyChecks.Single(c=>c.Check.Text==title).Check.Checked=value;
    internal void FriendlyMoreStyleForTest()=>FindFriendlyButton("More style options").PerformClick();
    internal void FriendlyOpenFoldForTest(string title)=>FindFriendlyButton(title).PerformClick();
    private Button FindFriendlyButton(string title)=>Descendants(this).OfType<Button>().First(b=>b.Text.StartsWith(title));
    private static IEnumerable<Control> Descendants(Control c)=>c.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(Descendants(child)));

    private Control BuildFriendlyLayout()
    {
        _controlsVisible=true;
        TableLayoutPanel root=new(){Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,BackColor=WindowColor};
        root.ColumnStyles.Add(new(SizeType.Percent,100));root.ColumnStyles.Add(new(SizeType.Absolute,326));
        root.RowStyles.Add(new(SizeType.Absolute,66));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,LookDockHeight));
        _headerPanel=BuildFriendlyHeader();root.Controls.Add(_headerPanel,0,0);root.SetColumnSpan(_headerPanel,2);
        root.Controls.Add(_renderer,0,1);
        _sidebarPanel=BuildFriendlyInspector();root.Controls.Add(_sidebarPanel,1,1);root.SetRowSpan(_sidebarPanel,2);
        _lookDockPanel=BuildFriendlyLookDock();root.Controls.Add(_lookDockPanel,0,2);
        _rootLayout=root;
        _friendlyTimer.Tick+=(_,_)=>
        {
            UpdateFriendlyStatus();
            if(_friendlyBlendAuto?.Checked==true&&InterfaceMotion.Enabled&&_friendlyFader is not null)
                _friendlyFader.Value=(int)((Math.Sin(_friendlyBlendClock.Elapsed.TotalSeconds*Math.Tau*.075)+1)*500);
        };_friendlyTimer.Start();return root;
    }
    private Control BuildFriendlyHeader()
    {
        TableLayoutPanel header=new(){Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Padding=new(14,8,14,8),BackColor=Color.FromArgb(10,14,21)};
        header.ColumnStyles.Add(new(SizeType.Absolute,178));header.ColumnStyles.Add(new(SizeType.Percent,100));header.ColumnStyles.Add(new(SizeType.AutoSize));
        header.Controls.Add(new DimensionalBrand{Dock=DockStyle.Fill,Compact=true},0,0);
        _romLabel.Text="No game open";_romLabel.Dock=DockStyle.Fill;_romLabel.AutoSize=false;_romLabel.AutoEllipsis=true;_romLabel.TextAlign=ContentAlignment.MiddleLeft;_romLabel.Margin=new(12,0,8,0);
        header.Controls.Add(_romLabel,1,0);
        FlowLayoutPanel actions=new(){AutoSize=true,WrapContents=false,FlowDirection=FlowDirection.LeftToRight,Anchor=AnchorStyles.Right,Margin=Padding.Empty};
        Button open=FriendlyButton("Open game",OpenRomDialog,true,112);actions.Controls.Add(open);
        _pauseButton.Width=82;_pauseButton.Height=36;_pauseButton.Margin=new(4,0,0,0);actions.Controls.Add(_pauseButton);_requiresGame.Add(_pauseButton);
        actions.Controls.Add(FriendlyButton("Focus view",ToggleCinematic,false,106));
        Button more=FriendlyButton("More ▾",()=>{},false,76);more.Click+=(_,_)=>_moreMenu.Show(more,new Point(0,more.Height));actions.Controls.Add(more);
        header.Controls.Add(actions,2,0);BuildFriendlyMenu();return header;
    }
    private void BuildFriendlyMenu()
    {
        _moreMenu.BackColor=PanelColor;_moreMenu.ForeColor=TextColor;_moreMenu.ShowImageMargin=false;_moreMenu.Font=new("Segoe UI",10);
        void Item(string text,Action action)=>_moreMenu.Items.Add(text,null,(_,_)=>action());
        Item("Open game…  Ctrl+O",OpenRomDialog);Item("Keyboard & controller…",OpenControlsEditor);
        Item("Fullscreen  F11",ToggleFullscreen);Item("Focus view  F10",ToggleCinematic);
        _controlsButton=FriendlyButton("Hide settings",ToggleControls);
        Item("Show / hide settings  Ctrl+Tab",ToggleControls);_moreMenu.Items.Add(new ToolStripSeparator());
        Item("Load a saved look…",LoadFriendlyLook);Item("Save this look…",SaveFriendlyLook);
        Item("Advanced: individual projections…  Ctrl+L",()=>OpenPresentationEditor(_friendlyObjectKey,_friendlyLayerKey));
        Item("Advanced: motion & geometry tools…",OpenDimensionStudio);
        Item("Advanced: game recognition editor…",OpenGameProfileEditor);
        Item("Which object types become 4D…",OpenProfileEditor);
        _moreMenu.Items.Add(new ToolStripSeparator());Item("Quick help",ShowFriendlyHelp);
        Item("Reset game  F2",()=>{_=StopRecordingAsync();_renderer.ResetRuntime();_emulator.Reset();});
    }
    private Control BuildFriendlyLookDock()
    {
        TableLayoutPanel dock=new(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new(14,5,14,8),BackColor=Color.FromArgb(10,15,22)};
        dock.RowStyles.Add(new(SizeType.Absolute,30));dock.RowStyles.Add(new(SizeType.Percent,100));dock.ColumnStyles.Add(new(SizeType.Percent,100));
        _stageStatus.Text="Pick a style · every button changes it live";_stageStatus.Dock=DockStyle.Fill;_stageStatus.AutoSize=false;_stageStatus.AutoEllipsis=true;_stageStatus.TextAlign=ContentAlignment.MiddleLeft;
        dock.Controls.Add(_stageStatus,0,0);
        TableLayoutPanel cards=new(){Dock=DockStyle.Fill,ColumnCount=6,RowCount=2,Margin=Padding.Empty};
        for(int i=0;i<6;i++)cards.ColumnStyles.Add(new(SizeType.Percent,100f/6));cards.RowStyles.Add(new(SizeType.Percent,50));cards.RowStyles.Add(new(SizeType.Percent,50));
        for(int i=0;i<LookCatalog.Names.Length;i++)
        {
            LookCard card=new(i){Dock=DockStyle.Fill,Compact=true,Text=FriendlyLooks.Names[i],Margin=new(3)};
            _friendlyTips.SetToolTip(card,LookCatalog.Names[i]+"\n"+FriendlyLooks.Descriptions[i]);
            card.Click+=(_,_)=>ApplyLook(card.LookIndex);cards.Controls.Add(card,i%6,i/6);_lookCards.Add(card);
        }
        Button gallery=FriendlyButton("Larger\npreviews…",OpenLookGallery);gallery.Dock=DockStyle.Fill;gallery.Margin=new(3);cards.Controls.Add(gallery,5,1);
        dock.Controls.Add(cards,0,1);return dock;
    }
    private Control BuildFriendlyInspector()
    {
        TableLayoutPanel inspector=new(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,BackColor=PanelColor,Padding=new(1,0,0,0)};
        inspector.RowStyles.Add(new(SizeType.Absolute,39));inspector.RowStyles.Add(new(SizeType.Percent,100));inspector.RowStyles.Add(new(SizeType.Absolute,66));
        _friendlyHelp.Dock=DockStyle.Fill;_friendlyHelp.AutoSize=false;_friendlyHelp.TextAlign=ContentAlignment.MiddleLeft;_friendlyHelp.Padding=new(14,0,4,0);inspector.Controls.Add(_friendlyHelp,0,0);
        NativeTheme.StyleTabs(_friendlyTabs);_friendlyTabs.ItemSize=new(75,38);_friendlyTabs.Font=new("Segoe UI",10);
        inspector.Controls.Add(_friendlyTabs,0,1);
        BuildFriendlyStyle();BuildFriendlyMotion();BuildFriendlyObjects();BuildFriendlySave();
        _statusLabel.AutoSize=false;_statusLabel.Dock=DockStyle.Fill;_statusLabel.MaximumSize=Size.Empty;_statusLabel.Padding=new(14,7,14,4);_statusLabel.Font=new("Segoe UI",8.5f);_statusLabel.Text="No game loaded. Open a .nes file to begin.";
        inspector.Controls.Add(_statusLabel,0,2);return inspector;
    }
    private FlowLayoutPanel FriendlyPage(string name)
    {
        TabPage page=new(name){BackColor=PanelColor,ForeColor=TextColor};FlowLayoutPanel stack=new(){Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new(16,16,10,16),BackColor=PanelColor};
        page.Controls.Add(stack);_friendlyTabs.TabPages.Add(page);return stack;
    }
    private static Button FriendlyButton(string text,Action click,bool primary=false,int width=260)
    {
        Button button=new(){Text=text,Width=width,Height=36,Margin=new(4,0,0,0),FlatStyle=FlatStyle.Flat,BackColor=primary?Lime:Color.FromArgb(23,34,46),ForeColor=primary?WindowColor:TextColor,Font=new("Segoe UI",9.5f,FontStyle.Bold),Cursor=Cursors.Hand,UseVisualStyleBackColor=false};
        button.FlatAppearance.BorderColor=primary?Lime:BorderColor;
        button.Paint+=(_,e)=>
        {
            if(button.Enabled)return;
            using SolidBrush fill=new(Color.FromArgb(23,34,46));e.Graphics.FillRectangle(fill,button.ClientRectangle);
            using Pen border=new(BorderColor);e.Graphics.DrawRectangle(border,0,0,button.Width-1,button.Height-1);
            TextRenderer.DrawText(e.Graphics,button.Text,button.Font,Rectangle.Inflate(button.ClientRectangle,-3,-3),Color.FromArgb(134,149,166),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
        };
        button.Click+=(_,_)=>click();return button;
    }
    private Button FriendlyAction(FlowLayoutPanel parent,string text,Action action,bool primary=false)
    {var button=FriendlyButton(text,action,primary);button.Margin=new(0,0,0,9);parent.Controls.Add(button);return button;}
    private static void FriendlyText(FlowLayoutPanel parent,string text,bool title=false)
    {parent.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new(260,160),ForeColor=title?TextColor:MutedColor,Font=new("Segoe UI",title?11:9,title?FontStyle.Bold:FontStyle.Regular),Margin=new(0,0,0,12)});}
    private TrackBar FriendlyKnob(FlowLayoutPanel parent,string title,string help,int low,int high,Func<int> read,Action<int> change,Func<int,string>? format=null)
    {
        var row=new FriendlySlider(title,help,low,high,read(),value=>{if(!_friendlySync&&!_batchingProjectionValues)change(value);},format);
        parent.Controls.Add(row);_friendlySliders.Add((row.Slider,read));
        row.Slider.MouseDown+=(_,_)=>{RememberFriendlyChange();_friendlyGesture=true;};row.Slider.MouseUp+=(_,_)=>{_friendlyGesture=false;_renderer.Focus();};return row.Slider;
    }
    private CheckBox FriendlyToggle(FlowLayoutPanel parent,string text,Func<bool> read,Action<bool> change,string help)
    {
        CheckBox check=new(){Text=text,Checked=read(),AutoSize=true,MaximumSize=new(260,50),ForeColor=TextColor,Font=new("Segoe UI",10),Margin=new(0,0,0,13),Cursor=Cursors.Hand,AccessibleDescription=help};
        _friendlyTips.SetToolTip(check,help);check.CheckedChanged+=(_,_)=>{if(!_friendlySync&&!_batchingProjectionValues)change(check.Checked);};parent.Controls.Add(check);_friendlyChecks.Add((check,read));return check;
    }
    private void FriendlyFold(FlowLayoutPanel parent,string title,Action<FlowLayoutPanel> build)
    {
        FlowLayoutPanel body=new(){AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Width=260,Margin=Padding.Empty,Visible=false};
        Button toggle=FriendlyAction(parent,title+"  ›",()=>{});toggle.Click+=(_,_)=>{body.Visible=!body.Visible;toggle.Text=title+(body.Visible?"  ▾":"  ›");};build(body);parent.Controls.Add(body);
    }
    private void BuildFriendlyStyle()
    {
        var page=FriendlyPage("Style");page.Controls.Add(new FriendlyCallout("1. Make it yours","Choose a style below the game. These sliders fine-tune the result."));
        _friendlyLookName.Margin=new(0,0,0,14);page.Controls.Add(_friendlyLookName);
        _wExtentSlider=FriendlyKnob(page,"Depth","Flat  ←  →  More dimensional",0,100,()=> (int)(_presentation.Depth*100),v=>EditFriendly(s=>s.Depth=v/100f));
        _opacitySlider=FriendlyKnob(page,"Projection visibility","Transparent  ←  →  Fully opaque",0,100,()=> (int)(_presentation.Opacity*100),v=>EditFriendly(s=>s.Opacity=v/100f));
        FriendlyKnob(page,"Lighting & glow","None  ←  →  More atmosphere",0,100,()=>_presentation.Effects.Enabled?(int)(_presentation.Effects.Glow*100):0,v=>EditFriendly(s=>{s.Effects.Enabled=v>0;s.Effects.Glow=v/100f;s.Effects.Lighting=v/100f;s.Effects.Shadows=v/150f;}));
        _friendlyUndoButton=FriendlyAction(page,"Undo last change",UndoFriendly);_friendlyUndoButton.Enabled=false;
        FriendlyAction(page,"Reset this look",ResetFriendlyLook);
        FriendlyFold(page,"More style options",advanced=>
        {
            _cameraSlider=FriendlyKnob(advanced,"Perspective","How close the dimensional camera feels",0,100,()=> (int)(_presentation.Perspective*100),v=>EditFriendly(s=>s.Perspective=v/100f));
            _crossSectionsSlider=FriendlyKnob(advanced,"Projection count","Fewer is clearer and usually faster",2,9,()=>_presentation.CrossSections,v=>EditFriendly(s=>s.CrossSections=v),v=>v.ToString());
            _projectionRotationSpreadSlider=FriendlyKnob(advanced,"Rotation variety","How different each projection looks",0,180,()=> (int)_presentation.RotationSpread,v=>EditFriendly(s=>s.RotationSpread=v),v=>v+"°");
            FriendlyKnob(advanced,"Lighting","Face shading",0,100,()=> (int)(_presentation.Effects.Lighting*100),v=>EditFriendly(s=>s.Effects.Lighting=v/100f));
            FriendlyKnob(advanced,"Glow","A soft accent around small objects",0,100,()=> (int)(_presentation.Effects.Glow*100),v=>EditFriendly(s=>s.Effects.Glow=v/100f));
            FriendlyKnob(advanced,"Shadows","Depth cues beneath small objects",0,100,()=> (int)(_presentation.Effects.Shadows*100),v=>EditFriendly(s=>s.Effects.Shadows=v/100f));
            FriendlyToggle(advanced,"Smooth style changes",()=>_presentation.Effects.SmoothTransitions,v=>EditFriendly(s=>s.Effects.SmoothTransitions=v),"Morph between styles instead of switching immediately.");
            _interfaceMotionCheckBox=FriendlyToggle(advanced,"Reduced motion",()=>!InterfaceMotion.Enabled,v=>{InterfaceMotion.Enabled=!v;InterfaceMotion.Save();_renderer.RefreshEffects();},"Freeze interface animation, object choreography, trails and audio motion.");
            FriendlyAction(advanced,"Save / load looks…",()=>ShowFriendlyPage(3));
        });
    }
    private void BuildFriendlyMotion()
    {
        var page=FriendlyPage("Motion");page.Controls.Add(new FriendlyCallout("2. Bring it to life","Choose what moves. Your visibility setting never changes by itself."));
        _cycleCheckBox=FriendlyToggle(page,"Animate this look",()=>_presentation.Animate,v=>{RememberFriendlyChange();StopFriendlyBlend();SetProjectionCycling(v,true);SyncFriendlyControls();},"Turn automatic shape and rotation movement on or off.");
        FriendlyKnob(page,"Movement speed","Slow  ←  →  Fast",0,100,()=>Math.Clamp((int)(_presentation.CycleSpeed*50),0,100),v=>EditFriendly(s=>s.CycleSpeed=v/50f));
        _randomizeCycleButton=FriendlyAction(page,"Surprise me · random movement",()=>{RememberFriendlyChange();RandomizeCycle();});
        FriendlyToggle(page,"Objects move independently",()=>_presentation.Dimensions.Choreography,v=>EditFriendly(s=>s.Dimensions.Choreography=v),"Players, pipes and scenery get different motion personalities.");
        FriendlyToggle(page,"Leave fading motion trails",()=>_presentation.Dimensions.Trails,v=>EditFriendly(s=>s.Dimensions.Trails=v),"Uses current artwork at earlier rotations, never old game screens.");
        FriendlyToggle(page,"React to game sound",()=>_presentation.Dimensions.AudioReactive,v=>EditFriendly(s=>s.Dimensions.AudioReactive=v),"A subtle response to Warp4D's own audio volume, including sound effects.");
        FriendlyAction(page,"Stop all visual movement",()=>EditFriendly(s=>{s.Animate=false;s.Dimensions.Choreography=s.Dimensions.Trails=s.Dimensions.AudioReactive=false;}));
        FriendlyFold(page,"Rotate precisely",advanced=>
        {
            FriendlyText(advanced,"Drag the game to rotate it. Ctrl locks W, Shift locks X, both lock Y.");advanced.Controls.Add(new DimensionCompass(_renderer){Width=260});
            _xyRotationSlider=FriendlyKnob(advanced,"XY rotation","Spatial plane",-180,180,()=> (int)_presentation.Rotation.XY,v=>EditFriendly(s=>{s.Rotation.XY=v;s.RotationAnimation.Enabled=false;}),v=>v+"°");
            _xzRotationSlider=FriendlyKnob(advanced,"XZ rotation","Spatial plane",-180,180,()=> (int)_presentation.Rotation.XZ,v=>EditFriendly(s=>{s.Rotation.XZ=v;s.RotationAnimation.Enabled=false;}),v=>v+"°");
            _yzRotationSlider=FriendlyKnob(advanced,"YZ rotation","Spatial plane",-180,180,()=> (int)_presentation.Rotation.YZ,v=>EditFriendly(s=>{s.Rotation.YZ=v;s.RotationAnimation.Enabled=false;}),v=>v+"°");
            _xwRotationSlider=FriendlyKnob(advanced,"XW rotation","Through the fourth dimension",-180,180,()=> (int)_presentation.Rotation.XW,v=>EditFriendly(s=>{s.Rotation.XW=v;s.RotationAnimation.Enabled=false;}),v=>v+"°");
            _ywRotationSlider=FriendlyKnob(advanced,"YW rotation","Through the fourth dimension",-180,180,()=> (int)_presentation.Rotation.YW,v=>EditFriendly(s=>{s.Rotation.YW=v;s.RotationAnimation.Enabled=false;}),v=>v+"°");
            _zwRotationSlider=FriendlyKnob(advanced,"ZW rotation","Through the fourth dimension",-180,180,()=> (int)_presentation.Rotation.ZW,v=>EditFriendly(s=>{s.Rotation.ZW=v;s.RotationAnimation.Enabled=false;}),v=>v+"°");
        });
        FriendlyFold(page,"Trail & sound settings",advanced=>
        {
            FriendlyKnob(advanced,"Trail length","More echoes cost more rendering time",1,6,()=>_presentation.Dimensions.TrailLength,v=>EditFriendly(s=>s.Dimensions.TrailLength=v),v=>v+" echoes");
            FriendlyKnob(advanced,"Trail visibility","Transparency of the echoes",0,100,()=> (int)(_presentation.Dimensions.TrailOpacity*100),v=>EditFriendly(s=>s.Dimensions.TrailOpacity=v/100f));
            FriendlyKnob(advanced,"Sound response","How strongly audio affects movement",0,100,()=> (int)(_presentation.Dimensions.AudioStrength*100),v=>EditFriendly(s=>s.Dimensions.AudioStrength=v/100f));
        });
        FriendlyFold(page,"Blend two saved looks",BuildFriendlyBlend);
    }
    private void BuildFriendlyObjects()
    {
        var page=FriendlyPage("Objects");page.Controls.Add(new FriendlyCallout("3. Tweak an object","Select artwork in the game, choose its shape, and preview the change immediately."));
        _friendlyProfile.MaximumSize=new(260,45);_friendlyProfile.Margin=new(0,0,0,12);page.Controls.Add(_friendlyProfile);
        _friendlySelectButton=FriendlyAction(page,"Select an object in the game",()=>{_renderer.PaintObjects=!_renderer.PaintObjects;_friendlySelectButton!.Text=_renderer.PaintObjects?"Selecting… click game artwork":"Select an object in the game";_friendlyPickHelp.Text=_renderer.PaintObjects?"Click an object. Drag empty space to rotate.":"Selection mode is off.";_renderer.Focus();},true);_requiresGame.Add(_friendlySelectButton);
        _friendlyPicked.MaximumSize=new(260,48);_friendlyPicked.Margin=new(0,0,0,7);page.Controls.Add(_friendlyPicked);
        _friendlyPickHelp.MaximumSize=new(260,70);_friendlyPickHelp.Margin=new(0,0,0,14);page.Controls.Add(_friendlyPickHelp);
        _friendlyClassScope=new(){Text="Apply to this entire object type",Checked=false,AutoSize=true,MaximumSize=new(260,45),Font=new("Segoe UI",9),Margin=new(0,0,0,12)};page.Controls.Add(_friendlyClassScope);_requiresSelection.Add(_friendlyClassScope);
        TableLayoutPanel shapes=new(){Width=260,Height=144,ColumnCount=2,RowCount=4,Margin=new(0,0,0,12)};shapes.ColumnStyles.Add(new(SizeType.Percent,50));shapes.ColumnStyles.Add(new(SizeType.Percent,50));
        for(int i=0;i<4;i++)shapes.RowStyles.Add(new(SizeType.Percent,25));string[] names=["Box","Round","Double circles","Slice","Ribbon","Lens","Unfold"];
        for(int i=0;i<7;i++){int mode=i;Button b=FriendlyButton(names[i],()=>PaintFriendlyShape((GeometryMode)mode));b.Dock=DockStyle.Fill;b.Margin=new(2);shapes.Controls.Add(b,i%2,i/2);_requiresSelection.Add(b);_friendlyGeometryButtons.Add(b);}page.Controls.Add(shapes);
        FriendlyKnob(page,"Slice position","Move the plane through the selected object",0,100,()=>FriendlySelectedGeometry()?.Phase is float p?(int)(p*100):50,v=>EditSelectedGeometry(g=>{g.Mode=GeometryMode.Slicing;g.Phase=v/100f;g.Animate=false;}));
        Button clear=FriendlyAction(page,"Remove this object's custom look",ClearFriendlyObjectLook);_requiresSelection.Add(clear);
        FriendlyFold(page,"Object movement",advanced=>
        {foreach(MotionStyle motion in Enum.GetValues<MotionStyle>()){var value=motion;Button b=FriendlyAction(advanced,motion.ToString(),()=>EditFriendly(s=>{if(_friendlyObjectKey is null||_friendlyObjectKind is null)return;s.Dimensions.Choreography=true;var dictionary=_friendlyClassScope?.Checked==true?s.Dimensions.ClassMotions:s.Dimensions.ObjectMotions;dictionary[_friendlyClassScope?.Checked==true?_friendlyObjectKind.ToString()!:_friendlyObjectKey]=new(){Style=value};}));_requiresSelection.Add(b);}});
        FriendlyFold(page,"Advanced object tools",advanced=>
        {
            Button layers=FriendlyAction(advanced,"Edit individual projections…",()=>OpenPresentationEditor(_friendlyObjectKey,_friendlyLayerKey));_requiresSelection.Add(layers);
            Button enable=FriendlyAction(advanced,"Enable 4D for this object type",()=>EditFriendly(s=>{if(_friendlyObjectKind is not null)s.ProjectionProfile.RuleFor(_friendlyObjectKind.Value).Enabled=true;}));_requiresSelection.Add(enable);
            FriendlyAction(advanced,"Choose which object types become 4D…",OpenProfileEditor);
            Button recognition=FriendlyAction(advanced,"Teach Warp4D a new game…",OpenGameProfileEditor);_requiresGame.Add(recognition);
            FriendlyText(advanced,"Most supported games are detected automatically. The recognition editor is only needed to identify new scenery or unsupported games.");
        });
    }
    private void BuildFriendlySave()
    {
        var page=FriendlyPage("Save");page.Controls.Add(new FriendlyCallout("Keep what you like","Capture gameplay, share a look, or save your progress."));
        _friendlySaveNotice.MaximumSize=new(260,46);_friendlySaveNotice.Margin=new(0,0,0,15);page.Controls.Add(_friendlySaveNotice);
        FriendlyAction(page,"Save a picture…  F12",CaptureScreenshot);
        _friendlyRecordButton=FriendlyAction(page,"Start recording video…",ToggleRecording,true);_requiresGame.Add(_friendlyRecordButton);
        _friendlyRecording.MaximumSize=new(260,54);_friendlyRecording.Margin=new(0,0,0,16);page.Controls.Add(_friendlyRecording);
        FriendlyText(page,"Video includes game sound, not menus. Click Stop recording to finish and save it.");
        FriendlyAction(page,"Save this look to a file…",SaveFriendlyLook);FriendlyAction(page,"Load a look from a file…",LoadFriendlyLook);
        FriendlyFold(page,"Game progress & controls",advanced=>
        {
            FriendlyText(advanced,"Save states remember game progress. Look files remember visual settings.");
            _stateSlot=new(){Minimum=1,Maximum=10,Value=1,Width=260,Margin=new(0,0,0,12),Font=new("Segoe UI",11)};_stateSlot.ValueChanged+=(_,_)=>_selectedStateSlot=(int)_stateSlot.Value;advanced.Controls.Add(_stateSlot);_requiresGame.Add(_stateSlot);
            Button save=FriendlyAction(advanced,"Save game progress  F5",()=>StateAction(true)),load=FriendlyAction(advanced,"Load game progress  F8",()=>StateAction(false));_requiresGame.Add(save);_requiresGame.Add(load);
            FriendlyAction(advanced,"Keyboard & controller settings…",OpenControlsEditor);
            FriendlyKnob(advanced,"Game volume","Audio playback level",0,100,()=>_inputSettings.Volume,v=>{_inputSettings.Volume=v;_emulator.SetVolume(v);SaveInputSettings();});
        });
        FriendlyFold(page,"Performance & accessibility",advanced=>
        {
            FriendlyToggle(advanced,"Keep performance smooth",()=>_presentation.Dimensions.AdaptiveQuality,v=>EditFriendly(s=>s.Dimensions.AdaptiveQuality=v),"Automatically reduce cosmetic detail when drawing becomes slow.");
            FriendlyKnob(advanced,"Target frame rate","A target, not a guarantee",20,120,()=>_presentation.Dimensions.TargetFps,v=>EditFriendly(s=>s.Dimensions.TargetFps=v),v=>v+" fps");
            FriendlyKnob(advanced,"Rendering detail","Lower detail is usually faster",1,4,()=>_presentation.RenderScale,v=>EditFriendly(s=>s.RenderScale=v),v=>v+"×");
            FriendlyToggle(advanced,"Use graphics acceleration",()=>_renderer.UseGpu,v=>{_renderer.UseGpu=v;_renderer.RefreshEffects();},"Switch this off if your graphics driver causes problems.");
            _performanceLabel.MaximumSize=new(260,160);advanced.Controls.Add(_performanceLabel);
            FriendlyAction(advanced,"Quick help & shortcuts",ShowFriendlyHelp);
        });
    }
    private void RememberFriendlyChange(){if(_friendlyGesture||_friendlySync)return;_friendlyUndo=_presentation.Clone();if(_friendlyUndoButton is not null)_friendlyUndoButton.Enabled=true;}
    private void EditFriendly(Action<PresentationSettings> edit)
    {
        if(_friendlySync||_closing)return;RememberFriendlyChange();StopFriendlyBlend();var settings=_presentation.Clone();edit(settings);settings.Normalize();
        ApplyFriendlySettings(settings);ProjectionSettingChanged();
    }
    private void ApplyFriendlySettings(PresentationSettings settings)
    {
        bool wasCycling=_projectionCycleClock.IsRunning;_presentation=settings;Volatile.Write(ref _projectionProfile,settings.ProjectionProfile.Clone());_renderer.ApplySettings(settings);
        if(settings.Animate){_projectionCycleClock.Start();if(!_emulator.IsLoaded)_projectionCycleTimer.Start();ApplyProjectionCycle(true);}
        else if(wasCycling){_projectionCycleClock.Stop();_projectionCycleTimer.Stop();}
        SyncPresentationSliders();SyncFriendlyControls();UpdateProfileLabel();
    }
    private void UndoFriendly()
    {
        if(_friendlyUndo is null)return;StopFriendlyBlend();var before=_friendlyUndo;_friendlyUndo=_presentation.Clone();ApplyPresentation(before);SavePresentation();_statusLabel.Text="Last visual change undone. Click again to redo.";
    }
    private void ResetFriendlyLook()
    {
        RememberFriendlyChange();ApplyPresentation(new(){ProjectionProfile=_presentation.ProjectionProfile.Clone(),RenderScale=_presentation.RenderScale});SavePresentation();_statusLabel.Text="Visual settings reset. Your game profile is unchanged. Undo is available.";
    }
    private void SyncFriendlyControls()
    {
        if(_friendlySync)return;_friendlySync=true;
        try
        {
            foreach(var item in _friendlySliders)SetSliderValue(item.Slider,item.Read());foreach(var item in _friendlyChecks)item.Check.Checked=item.Read();
            int index=Array.IndexOf(LookCatalog.Names,_presentation.Name);_friendlyLookName.Text=index>=0?FriendlyLooks.Names[index]:_presentation.Name=="Crossfade"?"Blended look":"Your custom look";
            foreach(var card in _lookCards){card.Chosen=card.LookIndex==index;card.Invalidate();}
            for(int i=0;i<_friendlyGeometryButtons.Count;i++)_friendlyGeometryButtons[i].BackColor=FriendlySelectedGeometry()?.Mode==(GeometryMode)i?Color.FromArgb(46,64,44):Color.FromArgb(23,34,46);
        }
        finally{_friendlySync=false;}
    }
    private void UpdateFriendlyStatus()
    {
        foreach(var c in _requiresGame)c.Enabled=_emulator.IsLoaded&&!_finishingRecording;
        foreach(var c in _requiresSelection)c.Enabled=_friendlyObjectKey is not null;
        if(_friendlyRecordButton is not null)_friendlyRecordButton.Text=_recorder is null?"Start recording video…":"Stop recording & save video";
        _friendlyRecording.Text=_recorder?.Status??(_finishingRecording?"Finishing your video…":"Not recording · Ctrl+F12 to start");
        _friendlySaveNotice.Text=_emulator.IsLoaded?"Your look saves automatically for this game.":"Open a game for per-game autosave. You can still save a look file.";
        if(_friendlyUndoButton is not null)_friendlyUndoButton.Enabled=_friendlyUndo is not null;
    }
    private void ShowFriendlyPage(int index)
    {if(_fullscreen)ToggleFullscreen();if(_cinematic)ToggleCinematic();if(!_controlsVisible)ToggleControls();_friendlyTabs.SelectedIndex=index;}
    private void PickFriendlyObject(string key,string layer)
    {
        SceneObject? item=_renderer.SceneObjects.FirstOrDefault(i=>i.PresentationKey==key);if(item is null)return;
        ShowFriendlyPage(2);_friendlyObjectKey=key;_friendlyObjectKind=item.Kind;_friendlyLayerKey=layer;
        _friendlyPicked.Text=item.Label;_friendlyPickHelp.Text=$"Selected: {ProjectionProfile.DisplayName(item.Kind)}. "+(item.ProjectionEnabled?"Choose a shape below.":"4D is disabled for this type. Enable it under Advanced object tools.");
        _renderer.SelectedObjectKey=key;_renderer.SelectedLayerKey=layer;_renderer.PaintObjects=false;
        if(_friendlySelectButton is not null)_friendlySelectButton.Text="Select another object";SyncFriendlyControls();UpdateFriendlyStatus();_renderer.Invalidate();
    }
    private void ClearFriendlySelection()
    {_friendlyObjectKey=null;_friendlyObjectKind=null;_friendlyLayerKey=null;_renderer.SelectedObjectKey=_renderer.SelectedLayerKey=null;_renderer.PaintObjects=false;_friendlyPicked.Text="No object selected";_friendlyPickHelp.Text="Click Select object, then click artwork in the game.";if(_friendlySelectButton is not null)_friendlySelectButton.Text="Select an object in the game";}
    private GeometrySettings? FriendlySelectedGeometry()
    {
        if(_friendlyObjectKey is null||_friendlyObjectKind is null)return null;
        return _presentation.ObjectGeometries.GetValueOrDefault(_friendlyObjectKey)??_presentation.ClassGeometries.GetValueOrDefault(_friendlyObjectKind.ToString()!)??_presentation.Geometry;
    }
    private void PaintFriendlyShape(GeometryMode mode)=>EditSelectedGeometry(g=>g.Mode=mode);
    private void EditSelectedGeometry(Action<GeometrySettings> edit)
    {
        if(_friendlyObjectKey is null||_friendlyObjectKind is null)return;
        EditFriendly(s=>
        {
            bool whole=_friendlyClassScope?.Checked==true;var dictionary=whole?s.ClassGeometries:s.ObjectGeometries;string key=whole?_friendlyObjectKind.ToString()!:_friendlyObjectKey;
            if(!dictionary.TryGetValue(key,out var geometry))dictionary[key]=geometry=(FriendlySelectedGeometry()??s.Geometry).Clone();
            if(whole)s.ObjectGeometries.Remove(_friendlyObjectKey);edit(geometry);
        });
    }
    private void ClearFriendlyObjectLook()
    {if(_friendlyObjectKey is not null&&_friendlyObjectKind is not null)EditFriendly(s=>{s.ObjectGeometries.Remove(_friendlyObjectKey);s.Dimensions.ObjectMotions.Remove(_friendlyObjectKey);if(_friendlyClassScope?.Checked==true)s.ClassGeometries.Remove(_friendlyObjectKind.ToString()!);});}
    private void SaveFriendlyLook()
    {
        ReleaseAllButtons();using SaveFileDialog dialog=new(){Filter="Warp4D look (*.warp4d-look.json)|*.warp4d-look.json",FileName="My Warp4D look.warp4d-look.json",Title="Save visual settings (not game progress)"};
        if(dialog.ShowDialog(this)==DialogResult.OK)try{PresentationSettingsStore.WriteToFile(dialog.FileName,_presentation);_statusLabel.Text="Look saved. Share this file with another Warp4D user.";}catch(Exception e)when(e is IOException or UnauthorizedAccessException){MessageBox.Show(this,e.Message,"Could not save look");}
    }
    private void LoadFriendlyLook()
    {
        ReleaseAllButtons();using OpenFileDialog dialog=new(){Filter="Warp4D look (*.json)|*.json",Title="Load visual settings"};
        if(dialog.ShowDialog(this)==DialogResult.OK)try{RememberFriendlyChange();var settings=PresentationSettingsStore.ReadFromFile(dialog.FileName);settings.ProjectionProfile=_presentation.ProjectionProfile.Clone();ApplyPresentation(settings);SavePresentation();_statusLabel.Text="Look loaded. Your game's recognition profile is unchanged.";}catch(Exception e)when(e is IOException or System.Text.Json.JsonException or InvalidDataException){MessageBox.Show(this,e.Message,"Could not load look");}
    }
    private void BuildFriendlyBlend(FlowLayoutPanel parent)
    {
        FriendlyText(parent,"Choose a start and finish look. Slide between them, or let the blend run automatically.");
        _friendlyBlendA=_presentation.Clone();_friendlyBlendB=LookCatalog.Create(6,_presentation);
        _friendlyBlendAName=MakeLabel("Start: current look",9,MutedColor);parent.Controls.Add(_friendlyBlendAName);
        FriendlyAction(parent,"Choose start look…",()=>LoadFriendlyBlendEndpoint(true));
        _friendlyBlendBName=MakeLabel("Finish: Double circles",9,MutedColor);parent.Controls.Add(_friendlyBlendBName);
        FriendlyAction(parent,"Choose finish look…",()=>LoadFriendlyBlendEndpoint(false));
        FriendlySlider blend=new("Blend amount","Start  ←  →  Finish",0,1000,0,BlendFriendly,v=>(v/10)+"%");parent.Controls.Add(blend);_friendlyFader=blend.Slider;
        _friendlyBlendAuto=new(){Text="Blend automatically",AutoSize=true,Font=new("Segoe UI",10),Margin=new(0,0,0,14)};
        _friendlyBlendAuto.CheckedChanged+=(_,_)=>{if(_friendlyBlendAuto.Checked){RememberFriendlyChange();_friendlyBlendClock.Restart();}else _friendlyBlendClock.Stop();};parent.Controls.Add(_friendlyBlendAuto);
        FriendlyAction(parent,"Save this blend as a look…",SaveFriendlyLook);
    }
    private void LoadFriendlyBlendEndpoint(bool start)
    {
        StopFriendlyBlend();using OpenFileDialog dialog=new(){Filter="Warp4D look|*.json",Title=start?"Choose the start look":"Choose the finish look"};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{var settings=PresentationSettingsStore.ReadFromFile(dialog.FileName);if(start){_friendlyBlendA=settings;_friendlyBlendAName!.Text="Start: "+settings.Name;}else{_friendlyBlendB=settings;_friendlyBlendBName!.Text="Finish: "+settings.Name;}}
        catch(Exception e)when(e is IOException or System.Text.Json.JsonException or InvalidDataException){MessageBox.Show(this,e.Message,"Could not load look");}
    }
    private void BlendFriendly(int value)
    {
        if(_friendlySync||_friendlyBlendA is null||_friendlyBlendB is null)return;
        if(_friendlyBlendAuto?.Checked!=true)RememberFriendlyChange();var preserved=_presentation;
        _presentation=LookCrossfader.Blend(_friendlyBlendA,_friendlyBlendB,value/1000f,preserved);_renderer.ApplyCrossfade(_friendlyBlendA,_friendlyBlendB,value/1000f,preserved);
        _projectionCycleClock.Stop();_projectionCycleTimer.Stop();SyncPresentationSliders();SyncFriendlyControls();ProjectionSettingChanged();
    }
    private void StopFriendlyBlend(){if(_friendlyBlendAuto is not null)_friendlyBlendAuto.Checked=false;_friendlyBlendClock.Stop();_dimensionStudio?.StopAutoBlend();}
    private void ShowFriendlyHelp()=>MessageBox.Show(this,
        "GET STARTED\n1. Open a .nes game or drop it onto the window.\n2. Click a style below the game.\n3. Use Style for depth and visibility, Motion for animation, Objects for individual artwork, and Save for captures.\n\nGAME CONTROLS\nArrow keys: move   X: A   Z: B   Enter: Start\nChange these under More → Keyboard & controller.\n\nUSEFUL SHORTCUTS\nHold F9: show original NES graphics\nF10: focus view   F11: fullscreen   Esc: return\nF12: picture   Ctrl+F12: video\nF5: save game progress   F8: load game progress\n\nLooks change the visuals. Game recognition profiles identify artwork. Neither contains a ROM.","Warp4D · Quick help",MessageBoxButtons.OK,MessageBoxIcon.Information);
}
