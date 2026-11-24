using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using HarmonyLib;
using Meitou.Content;
using Meitou.Data.Characters;
using Meitou.Data.Gameplay;
using Meitou.Game.Host;
using Meitou.Game.Host.Cameras;
using Meitou.Game.Rendering.Objects;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;
using Meitou.Transport.Verification;

internal static class RideQA
{
    private sealed record Case(string Id,string Name,float Height,float Span,bool Save=false,int Age=110);
    private sealed record Recipe(string Id,string Bone, Vector3 Offset,Quaternion Rotation,Vector3 Seat,float Yaw,string? Mesh);
    private enum Stage { Setup, Approach, Reposition, Mount, Straight, Turn, Saving, Loading, Remount, Exit, Foot, Park, Negative, Done }
    private static Case[] cases=[];
    private static readonly Dictionary<string,Recipe> recipes=[];
    private static int ix,frames,turns,attempts;
    private static Stage stage;
    private static readonly Stopwatch stageClock=new(), heartbeat=new();
    private static Vector3 site, forward, turn, parking, origin, corner, foot, stopped;
    private static Vector3 approachPoint;
    private static Vector3? drawnSeat;
    private static readonly bool followSeat=Environment.GetEnvironmentVariable("RVA_TRACK_BROADSIDE")=="1";
    private static float minApproach;
    private static Course? course;
    private sealed record Course(Vector3 Site, Vector3 Forward, Vector3 Parking);
    private static float startYaw,heading,maxSeat,maxMesh;
    private static long tick=-1, lastMeasure=-1;
    private static string? bag;
    private static string[] cargo=[];
    private static bool remounted,shot,entryShot,endShot,unowned,negativeRefused,partyParked;
#if NATIVE
    private static bool packCargoRequested;
#endif
    private static string lastNotice="";
    private static uint serial;
    private static string mode="";
    private static World? savedWorld;
    private static SaveCheck? saved;
    private static readonly HashSet<string> gaits=[];
    private static object? Field(object o,string n)=>AccessTools.Field(o.GetType(),n)?.GetValue(o);
    private static void Log(string text)=>Console.WriteLine("ROSTER "+text);
    private static void Require(bool v,string why){if(!v)throw new InvalidDataException(why);}
    private static void Go(Stage s){stage=s;stageClock.Restart();tick=-1;}
    private static Case C=>cases[ix];
    private static Recipe R=>recipes[C.Id];
    internal static void Arm()
    {
        string? file=Environment.GetEnvironmentVariable("RVA_CASES");
        if(file==null)return;
        cases=JsonSerializer.Deserialize<Case[]>(File.ReadAllBytes(file))!;
        mode=Environment.GetEnvironmentVariable("RVA_MODE")!;
        unowned=mode=="wild";
        var path=Path.Combine(Path.GetDirectoryName(ModList.FindMods(GameInstall.Locate()!)["Rideable Vanilla Animals"])!,"transport.json");
        using var doc=JsonDocument.Parse(File.ReadAllBytes(path));
        foreach(var m in doc.RootElement.GetProperty("mounts").EnumerateArray()){
            Vector3 V(string k){var v=m.GetProperty(k);return new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle());}
            var rot=V("saddleRotationDegrees")*(MathF.PI/180f);
            string? name=null;
            if(m.TryGetProperty("saddleMesh",out var mesh))name=Path.GetFileName(mesh.GetString())+DebrisPiece.TextureMark+Path.GetFileName(m.GetProperty("texture").GetString());
            var recipe=new Recipe(m.GetProperty("id").GetString()!,m.GetProperty("anchorBone").GetString()!,V("saddleOffset"),Quaternion.CreateFromYawPitchRoll(rot.Y,rot.X,rot.Z),V("seatOffset"),m.GetProperty("seatYawDegrees").GetSingle()*MathF.PI/180f,name);
            foreach(var id in m.GetProperty("animalRecordIds").EnumerateArray())recipes.Add(id.GetString()!,recipe);
        }
        var h=new Harmony("meitou.transport.roster-verification");
        HarmonyMethod H(string n)=>new(AccessTools.Method(typeof(RideQA),n));
        if(mode=="roster"){
            h.Patch(AccessTools.Method(typeof(SquadTemplate),"From"),postfix:H(nameof(PrepareSquad)));
            h.Patch(AccessTools.Method(typeof(NewGameStart),"From"),postfix:H(nameof(PlaceStart)));
        }
        if(unowned)h.Patch(AccessTools.Method(typeof(PopulationSystem),nameof(PopulationSystem.SpawnSandbox)),prefix:H(nameof(WildFixture)));
        var renderer=typeof(IMod).Assembly.GetType("Meitou.Game.Rendering.Characters.CharacterRenderer",true)!;
        var frame=H(nameof(Frame));frame.after=["meitou.transport"];
        h.Patch(AccessTools.Method(renderer,"CollectBolts"),postfix:frame);
        h.Patch(AccessTools.Method(typeof(World),"RunSlowWorld"),postfix:H(nameof(FindSite)));
        h.Patch(AccessTools.Method(typeof(World),"Post"),postfix:H(nameof(Message)));
        Log($"loaded {path}, {recipes.Count} eligible IDs, mode={mode}, cases={cases.Length}");
        Go(Stage.Setup);heartbeat.Start();
    }
    private static void PrepareSquad(ref SquadTemplate __result)
    {
        if(__result.Id!="47928-rebirth.mod")return;
        var original=__result.Animals.First();
        __result=__result with {Animals=cases.Select(c=>original with {Id=c.Id,V0=1,V1=1,V2=c.Age}).ToArray(),AnimalAgeRandom=0};
    }
    private static void WildFixture(ref string recordId,bool player)
    {
        if(!player && recordId=="1995-gamedata.base")recordId=cases[0].Id;
    }
    private static void PlaceStart(ref NewGameStart __result){
        if(__result.Name=="The Wandering Trader")__result=__result with {ForceStartPos=true,StartPosition=new(34595,-70128)};
    }
    private static void Message(string __0){
        lastNotice=__0;if(__0.Contains("squad",StringComparison.OrdinalIgnoreCase)){negativeRefused=true;Log("notice "+__0);}
    }
    private static void FindSite(World __instance)
    {
        if(Volatile.Read(ref course)!=null || __instance.Tick%10!=0)return;
        var t=__instance.Characters;
        for(int i=0;i<t.HighWater;i++){
            if(t.Cold(i) is not {IsPlayer:true,Animal:null})continue;
            var at=t.Next[i].Position; var walk=__instance.Walkability;
            if(unowned){Volatile.Write(ref course,new(at,Vector3.UnitX,at));return;}
            bool Ground(Vector3 pos,out Vector3 result){
                var near=walk.TerrainHeight(pos.X,pos.Z);
                bool valid=walk.TryStand(pos.X,pos.Z,near,out float y);
                result=pos with {Y=y};return valid && Math.Abs(y-near)<4 && y>104;
            }
            if(mode=="roster"){
                var pin=new Vector3(34595,0,-70128);
                if(!Ground(pin,out var start))return;
                var stable=pin-Vector3.UnitX*530;
                stable.Y=walk.TerrainHeight(stable.X,stable.Z);
                Volatile.Write(ref course,new(start,Vector3.UnitX,stable));Log($"Stoat course {start}, parked squad at {stable}");return;
            }
            foreach(float radius in new[]{120f,175f,245f,330f,400f})
                for(int a=0;a<8;a++){
                    var d=new Vector3(MathF.Cos(a*MathF.PI/4),0,MathF.Sin(a*MathF.PI/4));var left=new Vector3(-d.Z,0,d.X);
                    if(!Ground(at+radius*d,out var p)||!Ground(p+d*32,out _)||!Ground(p+d*72,out _)||!Ground(p+d*32+left*55,out _))continue;
                    Volatile.Write(ref course,new(p,d,at));Log($"course {p} from {at}, first {d}");return;
                }
            return;
        }
    }
    private static IEnumerable<string> Entries(Inventory? inventory,string prefix="")
    {
        if(inventory==null)yield break;
        foreach(var item in inventory.Items){
            var k=$"{prefix}/{item.Section}({item.X},{item.Y})/{item.Record}";
            yield return $"{k} x{item.Quantity}";foreach(var sub in Entries(item.Contents,k))yield return sub;
        }
    }
    private static void Action(object host,string label)
    {
        var menu=Field(host,"DebugMenu")!;var apply=AccessTools.Method(menu.GetType(),"DebugApply");
        var modding=Field(host,"Modding")!;
        var rows=(IReadOnlyList<ModDebugCommand>)AccessTools.Property(modding.GetType(),"ModDebugRows").GetValue(modding)!;
        int offset=((string[])AccessTools.Field(menu.GetType(),"DebugLabels").GetValue(null)!).Length;
        int index=Enumerable.Range(0,rows.Count).Single(i=>rows[i].Owner=="meitou.transport"&&rows[i].Label==label);
        var row=Enum.ToObject(apply.GetParameters()[0].ParameterType,offset+index);
        var result=(string?)apply.Invoke(menu,[row,"",default((string?,string)),null]);
        Require(result?.EndsWith("queued.",StringComparison.Ordinal)==true,label+" returned "+result);
    }
    private static void Shot(object host,string suffix)
    {
        var channel=Field(host,"AgentChannel")!;
        AccessTools.Field(channel.GetType(),"InterfaceHidden").SetValue(channel,true);
        var p=Path.GetFullPath($"{C.Id}-{suffix}.png");
        AccessTools.Field(channel.GetType(),"ShotPath").SetValue(channel,p);
        AccessTools.Field(host.GetType(),"ScreenshotRequested").SetValue(host,true);
    }
    private static void Camera(WorldSession s,CharacterSnapshot animal)
    {
        float height=C.Height*animal.Scale,span=C.Span*animal.Scale;
        // See the rider as well as the carrier. The Leviathan gets a shoulder portrait.
        var target=animal.Position+Vector3.UnitY*(span>130?height:height*.72f+2);
        float yaw=MathF.Atan2(forward.X,forward.Z)-.75f*MathF.PI, pitch=.16f;
        float dist=Math.Clamp(span*1.65f+27,50,172);
        if(followSeat && drawnSeat is {} pelvis){
            target=pelvis;
            yaw=animal.Yaw-MathF.PI/2;
            pitch=.28f;
            dist=Math.Clamp(span*.75f,70,105);
        }
        var eye=CameraState.EyeFor(target,yaw,pitch,dist);
        s.Camera.TakeOver(new(eye,Vector3.Normalize(target-eye),Vector3.UnitY,target,yaw,pitch,dist,true),strategy:false);
    }
    private static object? Entry(object renderer,object host,CharacterId id)
    {
        long key=(long)AccessTools.Method(host.GetType(),"DrawKey").Invoke(host,[id])!;
        var entries=(Array)Field(renderer,"_live")!;
        for(int i=0;i<(int)Field(renderer,"_liveCount")!;i++){
            var e=entries.GetValue(i)!;if(!(bool)Field(e,"PortraitOnly")! && (long)Field(e,"Key")! == key)return e;
        }
        return null;
    }
    private static Matrix4x4 Anchor(object e,string bone)
    {
        var rig=(SkeletonRig)Field(Field(e,"Asset")!,"Rig")!;int h=rig.Handle(bone)!.Value;
        var skin=((Matrix4x4[])Field(Field(e,"State")!,"Bones")!)[h];
        return Matrix4x4.CreateTranslation(rig.BindPosition[h])*skin*(Matrix4x4)Field(e,"World")!;
    }
    private static void Measure(object renderer,object host,CharacterSnapshot animal,CharacterSnapshot rider,List<DebrisPiece> pieces)
    {
        var carrier=Entry(renderer,host,animal.Id);var passenger=Entry(renderer,host,rider.Id);
        Require(carrier!=null && passenger!=null,"missing rendered character");
        var expected=Matrix4x4.CreateFromQuaternion(R.Rotation)*Matrix4x4.CreateTranslation(R.Offset)*Anchor(carrier!,R.Bone);
        Require(Matrix4x4.Decompose(expected,out var scale,out var rot,out var pos),"invalid carrier frame");
        var saddles=pieces.Where(p=>p.Mesh?.StartsWith("rva_trail.mesh",StringComparison.OrdinalIgnoreCase)==true).ToArray();
        Require(saddles.Length==(R.Mesh==null?0:1),"unexpected submitted saddle count "+saddles.Length);
        if(R.Mesh!=null){
            var p=saddles.Single();Require(p.Mesh==R.Mesh,"wrong saddle texture");
            var centre=((DebrisRenderer)Field(Field(host,"Gpu")!,"Debris")!).Centre(p.Mesh)!.Value;
            float err=Vector3.Distance(pos,p.Position-Vector3.Transform(centre*p.Scale,p.Rotation));maxMesh=Math.Max(maxMesh,err);
            Require(err<.08f && Math.Abs(Quaternion.Dot(rot,p.Rotation))>.999f && Math.Abs(scale.X-p.Scale)<.005f,$"saddle error {err:F5}");
        }
        var body=(Matrix4x4)Field(passenger!,"World")!;
        var actualPelvis=Anchor(passenger!,"Bip01 Pelvis").Translation;
        drawnSeat=actualPelvis;
        float seatErr=Vector3.Distance(actualPelvis,Vector3.Transform(R.Seat,expected));
        maxSeat=Math.Max(maxSeat,seatErr);
        var facing=Vector3.Transform(Vector3.TransformNormal(Vector3.UnitZ,Matrix4x4.CreateRotationY(R.Yaw)),rot);
        Require(seatErr<.08f && Math.Abs(Vector3.TransformNormal(Vector3.UnitX,body).Length()-rider.Scale)<.01f &&
            Vector3.Dot(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ,body)),facing)>.999f,$"pelvis, facing or size threshold: {seatErr:F5}");
#if NATIVE
        Require(rider.Anchor is {} anchor && anchor.Carrier==animal.Id && anchor.Bone==R.Bone,"native rider carrier or bone incorrect");
        Require(animal.BoneMeshes.Count(m=>m.Key=="meitou.transport.saddle")==saddles.Length,"native saddle count differs");
#endif
        if(bag!=null)Require(animal.Appearance?.AnimalBackpack?.StringId==bag && animal.Appearance.Parts.Count(p=>p.Record.StringId==bag && p.Mode==AttachMode.SharedSkeleton)==1,"stock bag appearance changed");
        frames++;if(stage==Stage.Turn)turns++;
        foreach(var clip in animal.Animations)gaits.Add(clip.Name);
    }
    private static void Frame(object __instance,List<DebrisPiece> into)
    {
        if(stage==Stage.Done||AccessTools.Property(__instance.GetType(),"Source").GetValue(__instance) is not Delegate {Target:{} host} ||
            host.GetType().Name!="GameHost" ||Field(host,"Session") is not WorldSession s)return;
        try{Step(__instance,host,s,into);}catch(Exception ex){
            Log($"FAIL {C.Id} {stage} {ex}");Shot(host,"failure");Go(Stage.Done);
        }
    }
    private static void Step(object renderer,object host,WorldSession s,List<DebrisPiece> into)
    {
        var w=s.World;var snap=s.CurrentSnapshot;
        if(snap.Tick==0 && stage==Stage.Setup)stageClock.Restart();
        if(stageClock.Elapsed.TotalSeconds>65)throw new TimeoutException(stage.ToString());
        if(stage==Stage.Loading && ReferenceEquals(w,savedWorld))return;
        s.Simulation.SetSpeed(2);
        var a=snap.Characters.FirstOrDefault(c=>c.IsPlayer!=unowned && c.RecordId==C.Id);
        var r=snap.Characters.FirstOrDefault(c=>c.IsPlayer && c.Appearance is {Animal:null});
        if(heartbeat.Elapsed.TotalSeconds>6){Log($"{C.Name}: {stage} tick={snap.Tick} animal={a?.Position} rider={r?.Position} sel={a?.Selected}/{r?.Selected} course={course?.Site}");heartbeat.Restart();}
        if(a==null || r==null || Volatile.Read(ref course) is not {} route)return;
        float distance=Vector3.Distance(a.Position,r.Position);bool submitted=into.Any(p=>p.Mesh?.StartsWith("rva_trail.mesh")==true);
        var inv=w.Characters.Cold(a.Id.Slot)?.Inventory;
        bool SameCargo()=>bag==inv?.Backpack?.Record && cargo.SequenceEqual(Entries(inv).Order());
        void Move(CharacterSnapshot who,Vector3 at)=>w.Commands.Enqueue(new MoveOrder([who.Id],at){Tick=w.Tick});
        void SelectRider()=>w.Commands.Enqueue(new SelectCommand([r.Id]){Tick=w.Tick});
        void Tele(CharacterSnapshot who,Vector3 at)=>w.Commands.Enqueue(new DebugTeleport([who.Id],at){Tick=w.Tick});
        bool After(int n){if(tick<0)tick=snap.Tick;return snap.Tick-tick>=n;}
        if(stage is Stage.Straight or Stage.Turn or Stage.Saving){
            Camera(s,a);
            if(snap.Tick!=lastMeasure){
                Require(a.Selected && distance<.13f,"lost occupied seat");
                Measure(renderer,host,a,r,into);lastMeasure=snap.Tick;
            }
        }
        switch(stage){
            case Stage.Setup:
                attempts=0;minApproach=float.MaxValue;drawnSeat=null;
                site=route.Site;forward=route.Forward;turn=new(-forward.Z,0,forward.X);parking=route.Parking;
#if NATIVE
                if(C.Id=="3999-gamedata.base" && inv?.Backpack?.Contents is { } contents && contents.Items.Count==0){
                    if(!packCargoRequested){
                        var (index,item)=inv.Shown().First(i=>i.Item.Section=="main");
                        var token=new InventoryItemToken(index,item.Record,item.Section,item.X,item.Y,item.Quantity,item.Quality);
                        w.Commands.Enqueue(new QuickMoveItem(a.Id,token,true,true){Tick=w.Tick});
                        packCargoRequested=true;Log($"moving {item.Record} x{item.Quantity} into the actual worn bag via QuickMoveItem");
                    }
                    Require(!After(90),"inventory move did not put cargo inside the stock bag");
                    return;
                }
                if(C.Id=="3999-gamedata.base")Require(inv?.Backpack?.Contents?.Items.Count>0,"native stock bag must carry cargo");
#endif
                if(!partyParked && mode=="roster"){
                    int n=0;
                    foreach(var other in snap.Characters.Where(c=>c.IsPlayer&&c.Appearance?.Animal!=null&&c.Id!=a.Id))Tele(other,parking+turn*(28*n++));
                    partyParked=true;
                }
                serial=a.PortraitSerial;bag=inv?.Backpack?.Record;cargo=[..Entries(inv).Order()];
                Log($"start {C.Id} {C.Name}, scale={a.Scale:F3} bag={bag??"none"} inventory={string.Join("|",cargo)}");
                if(C.Id=="3999-gamedata.base")Require(bag=="4002-gamedata.base","Pack Beast has no stock bag");
                if(C.Id=="47929-rebirth.mod")Require(bag=="43948-rebirth.mod","Pack Bull has no stock bag");
                Tele(a,site);Tele(r,site+forward*(C.Span*a.Scale>100?Math.Clamp(C.Span*a.Scale*.32f,28,80):3));SelectRider();Camera(s,a);Go(Stage.Approach);break;
            case Stage.Approach:
                Camera(s,a);
                if(!r.Selected||!After(4))return;
                minApproach=Math.Min(minApproach,distance);
                float reach=Math.Max(16f,22f*a.Scale);
                if(distance>reach || Math.Abs(a.Position.Y-r.Position.Y)>10){
                    if((snap.Tick-tick)%15==0)Move(r,a.Position);
                    if(snap.Tick-tick<100)return;
                    Log($"approach after walking: distance={distance:F2}, reach={reach:F2}, vertical gap={Math.Abs(r.Position.Y-a.Position.Y):F2}");
                }
                Require(!submitted,"loose saddle before mounting");negativeRefused=false;lastNotice="";Action(host,"Mount nearby animal");Go(unowned?Stage.Negative:Stage.Mount);break;
            case Stage.Reposition:
                Camera(s,a);
                if(!After(12) || Vector3.Distance(r.Position,approachPoint)>6 && snap.Tick-tick<120)return;
                Move(r,a.Position);Go(Stage.Approach);break;
            case Stage.Negative:
                if(!After(12))return;
                Require(!a.IsPlayer && !a.Selected && r.Selected && !submitted && negativeRefused,"unowned animal was not refused");
                Log("PASS negative: nearby unowned registered Garru refused by F10");Go(Stage.Done);break;
            case Stage.Mount:
                Camera(s,a);
                if(!a.Selected&&negativeRefused && stageClock.Elapsed.TotalSeconds>1.2){
                    if(C.Span*a.Scale>100 && attempts<2){
                        attempts++;approachPoint=a.Position+(attempts==1?turn:-forward)*(Math.Max(16,22*a.Scale)+35);
                        Log($"rejected approach {attempts}, nearest {minApproach:F2}; trying another side");
                        Move(r,approachPoint);Go(Stage.Reposition);break;
                    }
                    string reason=$"{lastNotice} separation={distance:F2} nearest={minApproach:F2} reach={Math.Max(16,22*a.Scale):F2} approaches={attempts+1}";
                    File.AppendAllText("failed.jsonl",JsonSerializer.Serialize(new{id=C.Id,C.Name,reason,scale=a.Scale})+Environment.NewLine);
                    throw new InvalidOperationException(reason);
                }
                if(!a.Selected||distance>.12f||(R.Mesh!=null && !submitted)||!After(3))return;
                origin=a.Position;w.Commands.Enqueue(new RunSpeedCommand([a.Id],-a.RunSpeed){Tick=w.Tick});
                Move(a,origin+forward*78);Go(Stage.Straight);break;
            case Stage.Straight:
                if(!entryShot && frames>=4 && Vector3.Distance(origin,a.Position)>6){Shot(host,"entry");entryShot=true;}
                if(Vector3.Distance(origin,a.Position)<22||frames<5)return;
                corner=a.Position;startYaw=a.Yaw;
                w.Commands.Enqueue(new RunSpeedCommand([a.Id],2){Tick=w.Tick});Move(a,corner+turn*75);Go(Stage.Turn);break;
            case Stage.Turn:
                heading=Math.Abs(MathF.IEEERemainder(a.Yaw-startYaw,MathF.Tau))*180/MathF.PI;
                if(!shot && heading>30){Shot(host,"turn");shot=true;}
                if(heading<72||turns<7||Vector3.Distance(corner,a.Position)<22)return;
                if(!File.Exists(C.Id+"-turn.png"))return;
                if(!endShot){Shot(host,"exit");endShot=true;return;}
                if(!File.Exists(C.Id+"-exit.png"))return;
                if(C.Save){savedWorld=w;saved=new SaveCheck(host,r);Log("saving "+saved.Directory);Go(Stage.Saving);}
                else{Action(host,"Dismount");Go(Stage.Foot);}
                break;
            case Stage.Saving:
                if(saved!.BeginLoadWhenWritten())Go(Stage.Loading);break;
            case Stage.Loading:
                if(!After(15))return;
                Require(!submitted && distance>2 && saved!.IsOriginalRider(r) && a.PortraitSerial==serial && SameCargo(),"reload state or bag contents differ");
#if NATIVE
                Require(r.Anchor==null && !a.BoneMeshes.Any(m=>m.Key=="meitou.transport.saddle"),"native attachment survived reload");
#endif
                SelectRider();Go(Stage.Remount);break;
            case Stage.Remount:
                if(!r.Selected)return;
                Action(host,"Mount nearby animal");Go(Stage.Exit);break;
            case Stage.Exit:
                Camera(s,a);
                if(!a.Selected||distance>.12f||(R.Mesh!=null&&!submitted)||!After(3))return;
                Measure(renderer,host,a,r,into);remounted=true;Action(host,"Dismount");Go(Stage.Foot);break;
            case Stage.Foot:
                if(!r.Selected||submitted||distance<2)return;
#if NATIVE
                Require(r.Anchor==null && !a.BoneMeshes.Any(m=>m.Key=="meitou.transport.saddle"),"native attachment survived dismount");
#endif
                if(tick<0){
                    foot=r.Position;stopped=a.Position;tick=snap.Tick;
                    if(C.Save)Move(r,foot+Vector3.Normalize((foot-a.Position) with {Y=0})*34);
                }
                if(snap.Tick-tick<5 || C.Save && Vector3.Distance(foot,r.Position)<5)return;
                Require(SameCargo(),"dismount changed bag or cargo");
                Require(Vector3.Distance(stopped,a.Position)<2,"animal continued walking after dismount");
                var result=new {id=C.Id,name=C.Name,fit=R.Id,scale=a.Scale,turnDegrees=heading,frames,turnFrames=turns,
                    maxPelvis=maxSeat,maxSaddle=R.Mesh==null?(float?)null:maxMesh,bag,cargo,remounted,footDistance=Vector3.Distance(foot,r.Position),
                    slot=C.Save?saved?.Directory:null,gaits=gaits.Order().ToArray(),image=C.Id+"-turn.png"};
                File.AppendAllText("rides.jsonl",JsonSerializer.Serialize(result)+Environment.NewLine);
                Log($"completed {C.Name}: {frames} poses, {heading:F1} degrees, pelvis {maxSeat:F5} saddle {(R.Mesh==null?"bareback":maxMesh.ToString("F5"))}");
                if(ix+1==cases.Length){Log("PASS all "+cases.Length+" rides");Go(Stage.Done);break;}
                Tele(a,parking+turn*(ix*27));Go(Stage.Park);break;
            case Stage.Park:
                if(!After(5)||Vector3.Distance(a.Position,site)<45)return;
                ix++;frames=turns=0;maxSeat=maxMesh=heading=0;entryShot=endShot=shot=remounted=false;lastMeasure=-1;gaits.Clear();saved=null;Go(Stage.Setup);break;
        }
    }
}
