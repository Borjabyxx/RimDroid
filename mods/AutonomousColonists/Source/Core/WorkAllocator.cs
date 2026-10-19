namespace AutonomousColonists.Core;

internal sealed record Worker(string Id, float Aptitude, int Current, bool Available = true,
    bool Disabled = false, bool Locked = false, bool Incumbent = false);
internal sealed record WorkRequest(string Name, int Population, Worker[] Workers, int Requested = -1,
    bool Skilled = true);
internal sealed record Assignment(string Pawn, string Work, int Priority);

/// <summary>Deterministic, bounded coverage planning. Never calls a JobDriver or enables a zero cell.</summary>
internal static class WorkAllocator
{
    public static bool Emergency(string name) => name is "Patient" or "PatientBedRest" or "BedRest" or "Firefighter";
    public static bool Housework(string name) => name is "Hauling" or "Cleaning";
    public static bool Universal(string name) => name == "BasicWorker";
    public static bool UsesCoverage(string name, bool skilled) => !Emergency(name) && !Universal(name) && (Housework(name) || skilled);
    public static int Order(string name) => name switch
    {
        "Doctor" => 0, "Cooking" => 1, "Growing" => 2, "Construction" => 3,
        "Handling" => 4, "Crafting" => 5, "Research" => 6, "Cleaning" => 98, "Hauling" => 99, _ => 20
    };

    public static int Coverage(string name, int population) => name switch
    {
        "Doctor" => population >= 8 ? 2 : 1,
        "Cooking" or "Growing" or "Construction" => population >= 10 ? 3 : population >= 5 ? 2 : 1,
        "Hauling" or "Cleaning" => Math.Max(1, population / 3),
        _ => 1
    };

    public static List<Assignment> Allocate(IEnumerable<WorkRequest> requests, float hysteresis = 6f)
    {
        var output = new List<Assignment>();
        var loads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (WorkRequest work in requests.OrderBy(w => Order(w.Name)).ThenBy(w => w.Name, StringComparer.Ordinal))
        {
            if (work.Requested == 0) continue; // User has paused management of this work type.
            bool emergency = Emergency(work.Name), universal = Universal(work.Name), house = Housework(work.Name);
            bool role = UsesCoverage(work.Name, work.Skilled);
            int manualCoverage = 0;
            foreach (Worker worker in work.Workers)
            {
                if (!worker.Available || worker.Disabled || !worker.Locked || worker.Current < 1 || worker.Current > (house ? 3 : 2)) continue;
                manualCoverage++;
                if (role) loads[worker.Id] = loads.GetValueOrDefault(worker.Id) + 1;
            }
            float Score(Worker w)
            {
                int load = loads.GetValueOrDefault(w.Id);
                return w.Aptitude - load * (house ? 8f : 4f) - Math.Max(0, load - 2) * 4f
                    + (w.Incumbent ? Math.Clamp(hysteresis, 0f, 20f) : 0f);
            }
            Worker[] candidates = work.Workers.Where(w => w.Available && !w.Disabled && !w.Locked && w.Current is >= 1 and <= 4)
                .OrderByDescending(Score).ThenBy(w => w.Id, StringComparer.Ordinal).ToArray();
            int desired = work.Requested < 0 ? Coverage(work.Name, work.Population) : work.Requested;
            // Locked player assignments provide coverage, but are never modified or counted as available if disabled.
            int slots = Math.Clamp(desired - manualCoverage, 0, candidates.Length);
            for (int index = 0; index < candidates.Length; index++)
            {
                Worker pawn = candidates[index];
                int priority = emergency ? 1 : universal ? 2 : !role ? 4 : house ? (index < slots ? 3 : 4)
                    : index < slots ? (index == 0 && manualCoverage == 0 ? 1 : 2)
                    : index == slots ? 3 : 4;
                output.Add(new(pawn.Id, work.Name, priority));
                if (role && index < slots) loads[pawn.Id] = loads.GetValueOrDefault(pawn.Id) + 1;
            }
        }
        return output;
    }
}
