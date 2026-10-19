using System.Diagnostics;
using System.Text.Json;
using AutonomousColonists.Core;

static class Suite
{
    static Worker W(string id, float score = 10, int priority = 3, bool available = true,
        bool disabled = false, bool locked = false, bool incumbent = false) => new(id, score, priority, available, disabled, locked, incumbent);
    static List<Assignment> Plan(params WorkRequest[] requests) => WorkAllocator.Allocate(requests);
    static WorkRequest Work(string name, params Worker[] workers) => new(name, workers.Length, workers);
    static void Check(bool value, string reason = "invariant failed") { if (!value) throw new Exception(reason); }
    static int Priority(List<Assignment> a, string id, string work = "Doctor") => a.Single(x => x.Pawn == id && x.Work == work).Priority;
    static PriorityLedger Owned(string key = "A|Doctor", int original = 3, int applied = 1)
    { var l = new PriorityLedger(); l.Record(key, original, applied); return l; }

    public static int Main(string[] args)
    {
        var tests = new (string, Action)[]
        {
            ("best eligible doctor leads", () => Check(Priority(Plan(Work("Doctor", W("A", 6), W("B", 25))), "B") == 1)),
            ("disabled strongest worker never assigned", () => {
                var a=Plan(Work("Doctor",W("A",40,disabled:true),W("B",4)));
                Check(a.All(x=>x.Pawn!="A") && Priority(a,"B")==1);
            }),
            ("priority zero never enabled", () => Check(Plan(Work("Cooking",W("A",40,0))).Count==0)),
            ("out of range cell never written", () => Check(Plan(Work("Doctor",W("A",40,5))).Count==0)),
            ("locked cell never assigned", () => Check(Plan(Work("Doctor",W("A",40,locked:true))).Count==0)),
            ("drafted stressed mental downed workers unavailable", () => Check(Plan(Work("Doctor",W("A",40,available:false))).Count==0)),
            ("coverage uses whole population then clamps capacity", () => {
                var a=Plan(new WorkRequest("Cooking",20,[W("A"),W("B")]));
                Check(a.Count==2 && a.All(x=>x.Priority<=2));
            }),
            ("player requested demand caps specialist count", () => {
                var a=Plan(new WorkRequest("Cooking",20,[W("A",20),W("B",15),W("C",10),W("D",5)],1));
                Check(a.Count(x=>x.Priority<=2)==1 && a.Count(x=>x.Priority==3)==1);
            }),
            ("paused work type untouched", () => Check(Plan(new WorkRequest("Cooking",5,[W("A")],0)).Count==0)),
            ("manual specialist provides coverage", () => {
                var a=Plan(new WorkRequest("Doctor",5,[W("A",20,1,locked:true),W("B",10),W("C",5)]));
                Check(a.Count(x=>x.Priority<=2)==0 && Priority(a,"B")==3);
            }),
            ("disabled manual specialist is not coverage", () => Check(Priority(Plan(Work("Doctor",W("A",40,1,disabled:true,locked:true),W("B",10))),"B")==1)),
            ("unavailable manual specialist is not coverage", () => Check(Priority(Plan(Work("Doctor",W("A",40,1,available:false,locked:true),W("B",10))),"B")==1)),
            ("specialists and backup are distinct", () => {
                var a=Plan(new WorkRequest("Cooking",10,[W("A",30),W("B",25),W("C",20),W("D",15),W("E",10)]));
                Check(a.Count(x=>x.Priority<=2)==3 && a.Count(x=>x.Priority==3)==1 && a.Count(x=>x.Priority==4)==1);
            }),
            ("hysteresis retains near tied incumbent", () => {
                var a=Plan(Work("Doctor",W("A",20,incumbent:true),W("B",25)));
                Check(Priority(a,"A")==1);
            }),
            ("clear skill improvement can replace incumbent", () => Check(Priority(Plan(Work("Doctor",W("A",20,incumbent:true),W("B",30))),"B")==1)),
            ("one excellent worker not given every near tied role", () => {
                var a=Plan(Work("Doctor",W("A",20),W("B",19)),Work("Cooking",W("A",20),W("B",19)));
                Check(a.Single(x=>x.Work=="Doctor"&&x.Priority==1).Pawn != a.Single(x=>x.Work=="Cooking"&&x.Priority==1).Pawn);
            }),
            ("housework uses spare capacity", () => {
                var a=Plan(Work("Doctor",W("A",30),W("B",5)),Work("Cleaning",W("A",1),W("B",1)));
                Check(Priority(a,"B","Cleaning")==3 && Priority(a,"A","Cleaning")==4);
            }),
            ("manual roles count toward specialist load", () => {
                var a=Plan(Work("Doctor",W("A",30,1,locked:true),W("B",5,0)),Work("Cooking",W("A",20),W("B",20)));
                Check(Priority(a,"B","Cooking")==1);
            }),
            ("manual cleaner counts toward housekeeping coverage", () => {
                var a=Plan(Work("Cleaning",W("A",1,3,locked:true),W("B",1)));
                Check(Priority(a,"B","Cleaning")==4);
            }),
            ("manual cleaning load spreads hauling to another pawn", () => {
                var a=Plan(Work("Cleaning",W("A",1,3,locked:true),W("B",1)),Work("Hauling",W("A",1),W("B",1)));
                Check(Priority(a,"B","Hauling")==3 && Priority(a,"A","Hauling")==4);
            }),
            ("emergencies and universal tasks preserved", () => {
                var a=Plan(Work("Patient",W("A")),Work("Firefighter",W("A")),Work("BasicWorker",W("A")));
                Check(a.Where(x=>x.Work!="BasicWorker").All(x=>x.Priority==1));
                Check(Priority(a,"A","BasicWorker")==2);
            }),
            ("unknown unskilled mod work remains fallback", () => Check(Plan(new WorkRequest("Custom",5,[W("A"),W("B")],Skilled:false)).All(x=>x.Priority==4))),
            ("empty colony and all incapable are safe", () => Check(Plan(Work("Doctor")).Count==0 && Plan(Work("Doctor",W("A",disabled:true))).Count==0)),
            ("manual change before immediate recalc is locked", () => {
                var l=Owned(); Check(!l.CanManage("A|Doctor",2) && l.Locks.Contains("A|Doctor"));
            }),
            ("manually enabling a previously zero cell is protected", () => {
                var l=new PriorityLedger(); l.Observe("A|Doctor",0);
                Check(!l.CanManage("A|Doctor",2));
            }),
            ("manual edits to paused unowned work are protected", () => {
                var l=new PriorityLedger(); l.Observe("A|Doctor",3);
                Check(!l.CanManage("A|Doctor",1));
            }),
            ("mod writes update observation baseline", () => {
                var l=new PriorityLedger(); l.Observe("A|Doctor",3); l.Record("A|Doctor",3,1);
                Check(l.CanManage("A|Doctor",1) && l.Locks.Count==0);
            }),
            ("restoration does not create a false manual lock", () => {
                var l=Owned(); int baseline=l.RestoreValue("A|Doctor",1)!.Value; l.Release("A|Doctor");
                Check(l.CanManage("A|Doctor",baseline));
            }),
            ("manual disabling is retained", () => { var l=Owned(); Check(!l.CanManage("A|Doctor",0) && l.RestoreValue("A|Doctor",0)==null); }),
            ("conditional restore returns original", () => Check(Owned().RestoreValue("A|Doctor",1)==3)),
            ("manual edit never restored over", () => Check(Owned().RestoreValue("A|Doctor",4)==null)),
            ("unowned cells never restored", () => Check(new PriorityLedger().RestoreValue("A|Doctor",1)==null)),
            ("repeated recalculation preserves first baseline", () => {
                var l=Owned(); l.Record("A|Doctor",1,2); Check(l.RestoreValue("A|Doctor",2)==3);
            }),
            ("locks survive exclusion suspension and map transfer", () => {
                var l=Owned(); l.Observe("A|Doctor",4); l.Release("A|Doctor");
                Check(!l.CanManage("A|Doctor",4) && l.Original.Count==0 && l.Applied.Count==0);
            }),
            ("save representation retains ownership and locks", () => {
                var l=Owned(); l.Locks.Add("B|Cooking");
                // Tests the value representation; Verse Scribe itself requires a real game.
                var json=JsonSerializer.Serialize(new Saved(l.Original,l.Applied,l.Locks.ToList(),l.Seen));
                var s=JsonSerializer.Deserialize<Saved>(json)!;
                var restored=new PriorityLedger(s.Original,s.Applied,s.Locks,s.Seen);
                Check(restored.RestoreValue("A|Doctor",1)==3 && !restored.CanManage("B|Cooking",2));
            }),
            ("forgetting dead pawn does not remove similar ID", () => {
                var l=Owned(); l.Record("AA|Doctor",4,1); l.Locks.Add("A|Cooking"); l.ForgetPawn("A");
                Check(l.Applied.Count==1 && l.Applied.ContainsKey("AA|Doctor") && l.Locks.Count==0);
            }),
            ("restoration does not overwrite late manual edit", () => {
                var l=Owned(); Check(l.RestoreValue("A|Doctor",1)==3); Check(l.RestoreValue("A|Doctor",2)==null);
            }),
            ("random colonies invariant and permutation tests", Fuzz),
            ("unchanged snapshots stabilize after one refresh", Stable),
            ("stress recovery uses separate thresholds", () => {
                Check(Availability.Paused(false,0.19f,1f,false));
                Check(Availability.Paused(false,0.25f,1f,true));
                Check(!Availability.Paused(false,0.30f,0.25f,true));
                Check(Availability.Paused(false,1f,0.14f,false));
                Check(Availability.Paused(false,1f,0.20f,true));
                Check(Availability.Paused(true,1f,1f,false));
            })
        };
        var results=new List<object>();
        int failures=0;
        foreach(var (name, action) in tests)
        {
            try { action(); Console.WriteLine("PASS "+name); results.Add(new {name,passed=true,error=(string?)null}); }
            catch(Exception e) { failures++; Console.WriteLine("FAIL "+name+": "+e); results.Add(new{name,passed=false,error=e.Message}); }
        }
        var benchmarks=Benchmarks();
        string? report=args.Length==2 && args[0]=="--report" ? args[1] : null;
        if(report!=null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
            File.WriteAllText(report,JsonSerializer.Serialize(new{tests=results,failures,benchmarks,gameExecuted=false,androidExecuted=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        Console.WriteLine($"{tests.Length-failures}/{tests.Length} tests passed; actual game and Android tests pending.");
        return failures==0?0:1;
    }
    record Saved(Dictionary<string,int> Original,Dictionary<string,int> Applied,List<string> Locks,Dictionary<string,int> Seen);
    static WorkRequest[] Snapshot(int n, int seed)
    {
        var r=new Random(seed);
        string[] names=["Doctor","Cooking","Growing","Construction","Handling","Crafting","Research","Smithing","Tailoring","PlantCutting","Mining","Hunting","Art","Patient","BedRest","Firefighter","BasicWorker","Cleaning","Hauling","Custom"];
        return names.Select(name=>new WorkRequest(name,n,Enumerable.Range(0,n).Select(i=>
            W("P"+i.ToString("D3"),r.Next(0,41),r.Next(0,5),available:r.Next(8)!=0,disabled:r.Next(10)==0,locked:r.Next(10)==0)).ToArray(),Skilled:name!="Custom")).ToArray();
    }
    static void Fuzz()
    {
        for(int seed=0;seed<200;seed++)
        {
            var works=Snapshot(seed%51,seed);
            var a=WorkAllocator.Allocate(works);
            Check(a.Select(x=>(x.Pawn,x.Work)).Distinct().Count()==a.Count,"duplicate assignment");
            foreach(var x in a)
            {
                var w=works.Single(w=>w.Name==x.Work).Workers.Single(w=>w.Id==x.Pawn);
                Check(w.Available&&!w.Disabled&&!w.Locked&&w.Current>0&&x.Priority is >=1 and <=4,"eligibility or priority violation");
            }
            var reversed=works.Reverse().Select(w=>w with{Workers=w.Workers.Reverse().ToArray()});
            Check(a.SequenceEqual(WorkAllocator.Allocate(reversed)),"input ordering changed result");
        }
    }
    static void Stable()
    {
        for(int seed=0;seed<50;seed++)
        {
            var works=Snapshot(20,seed);
            var a=WorkAllocator.Allocate(works);
            for(int iteration=0;iteration<5;iteration++)
            {
                works=works.Select(w=>w with{Workers=w.Workers.Select(p=>{
                    var last=a.FirstOrDefault(x=>x.Work==w.Name&&x.Pawn==p.Id);
                    return p with{Incumbent=last!=null&&last.Priority<=(WorkAllocator.Housework(w.Name)?3:2)};
                }).ToArray()}).ToArray();
                var next=WorkAllocator.Allocate(works);
                Check(a.SequenceEqual(next),$"oscillation seed={seed} iteration={iteration}"); a=next;
            }
        }
    }
    static List<object> Benchmarks()
    {
        var result=new List<object>();
        foreach(int n in new[]{1,5,20,50})
        {
            var works=Snapshot(n,42);
            for(int i=0;i<50;i++) WorkAllocator.Allocate(works);
            var samples=new double[500];
            long bytes=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<samples.Length;i++) { long start=Stopwatch.GetTimestamp(); WorkAllocator.Allocate(works); samples[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
            bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            Array.Sort(samples);
            Console.WriteLine($"BENCH x64 allocator only {n} pawns, {works.Length} work types: median {samples[250]:F4} ms; p95 {samples[475]:F4} ms; {bytes/500} bytes/call");
            result.Add(new{pawns=n,workTypes=works.Length,samples=500,medianMs=samples[250],p95Ms=samples[475],bytesPerCall=bytes/500,scope="Linux x64 pure allocator; excludes game adapter, Scribe, rendering and Android"});
        }
        return result;
    }
}
