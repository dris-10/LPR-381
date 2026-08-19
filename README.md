# LPR381 Project — Linear & Integer Programming Solver

Console application (`solve.exe`) that solves Linear and Integer Programming models, displays all tableau iterations, and performs sensitivity analysis.

**Group members**

| Role | Name | Owns |
|---|---|---|
| 1. Core & I/O | Dristen Venter | `Solver.Core/`, `Solver.App/` |
| 2. Primal Simplex & Branch and Bound | Iwan Groenewald | 4 files in `Solver.Algorithms/` |
| 3. Knapsack & Cutting Plane | Marco Rentroia | 3 files in `Solver.Algorithms/` |
| 4. Sensitivity Analysis | Xander Oosthuyzen | `Solver.Sensitivity/` |

**Target framework:** `net10.0` — *confirm at the Day 1 meeting.* Everyone must target the same version. If any member's Visual Studio cannot select it, drop to the highest version all four can hit and update this line.

---

## Solution & file structure

Ownership is **per folder / per file**. You edit only what you own.

```
LPR381Project/
├── LPR381Project.sln
│
├── Solver.Core/                    ← OWNER: Person 1
│   ├── Models/
│   │   ├── LPModel.cs
│   │   ├── Constraint.cs
│   │   ├── SignRestriction.cs      (enum: Positive, Negative, Urs, Int, Bin)
│   │   ├── RelationType.cs         (enum: LessEqual, GreaterEqual, Equal)
│   │   └── ObjectiveType.cs        (enum: Max, Min)
│   ├── Tableau/
│   │   ├── Tableau.cs
│   │   ├── TableauSnapshot.cs
│   │   └── PivotOperation.cs
│   ├── Results/
│   │   ├── SolutionResult.cs
│   │   ├── SolutionStatus.cs       (enum: Optimal, Infeasible, Unbounded)
│   │   └── IterationLog.cs
│   ├── Interfaces/
│   │   ├── ISolver.cs
│   │   └── ISensitivityAnalyzer.cs
│   └── IO/
│       ├── InputFileParser.cs
│       ├── OutputFileWriter.cs
│       └── CanonicalFormBuilder.cs
│
├── Solver.Algorithms/
│   ├── PrimalSimplex.cs            ← OWNER: Person 2
│   ├── RevisedPrimalSimplex.cs     ← OWNER: Person 2
│   ├── BranchAndBoundSimplex.cs    ← OWNER: Person 2
│   ├── BranchNode.cs               ← OWNER: Person 2
│   ├── KnapsackBranchAndBound.cs   ← OWNER: Person 3
│   ├── KnapsackNode.cs             ← OWNER: Person 3
│   └── CuttingPlane.cs             ← OWNER: Person 3
│
├── Solver.Sensitivity/             ← OWNER: Person 4
│   ├── SensitivityAnalyzer.cs
│   ├── RangeCalculator.cs
│   ├── ShadowPriceCalculator.cs
│   ├── DualityAnalyzer.cs
│   └── ModelModifier.cs
│
├── Solver.App/                     ← OWNER: Person 1
│   ├── Program.cs
│   ├── Menu/
│   │   ├── MainMenu.cs
│   │   ├── AlgorithmMenu.cs
│   │   └── SensitivityMenu.cs
│   └── Display/
│       ├── TableauFormatter.cs
│       └── ConsoleHelper.cs
│
└── TestData/                       ← shared, append-only
    ├── knapsack.txt
    ├── standard_max.txt
    ├── infeasible.txt
    └── unbounded.txt
```

### The ownership rule

**You only edit files in folders you own.** If you need something changed in `Solver.Core`, message Person 1 — do not edit it yourself. This single rule prevents most merge conflicts.

`TestData/` is append-only: add new files freely, don't modify or delete anyone else's.

---

## Interfaces

Person 1 writes these on **Day 1**. Once merged to `main`, they are **frozen** — changes require telling the whole group.

```csharp
public interface ISolver
{
    string AlgorithmName { get; }
    bool CanSolve(LPModel model);
    SolutionResult Solve(LPModel model);
}
```

Interfaces exist so Persons 2, 3 and 4 can start before the implementations are finished. Person 4 builds against a hardcoded fake `SolutionResult` from Day 2 rather than waiting until Day 8.

---

## Setup

1. **Clone to a path outside OneDrive / Google Drive.** Example: `C:\Dev\LPR381Project`
2. Open `LPR381Project.sln` in Visual Studio.
3. Build. Confirm the output is `solve.exe`.
4. Apply the Visual Studio settings below **before writing any code**.

### Do not put the repo in OneDrive

OneDrive sync corrupts Git repos. It syncs the `.git` folder mid-operation, locks `bin/` and `obj/` during builds, and creates conflict copies like `Program-DESKTOP-ABC123.cs` that break the build. GitHub is the backup — that's what it's for.

### Required Visual Studio settings

- **Tools → Options → Text Editor → C# → Advanced**
  - Turn **OFF** "Run code cleanup profile on save"
  - Turn **OFF** "Format document on save"
- **Tools → Options → Source Control → Git Global Settings**
  - Set pull behaviour to **Merge**, not Rebase

Format-on-save reformats entire files including lines you never touched. Git then sees 200 changed lines instead of 3, and you get conflicts on code you didn't write. Everyone must turn this off.

Do not run "Format Document" or "Remove unused usings" on files you don't own.

---

## Branches

```
main                    ← protected, PR only, must always build
└── dev                 ← integration branch
    ├── feature/core-io           (Person 1)
    ├── feature/simplex-bnb       (Person 2)
    ├── feature/knapsack-cutting  (Person 3)
    └── feature/sensitivity       (Person 4)
```

Six branches total. One per person, plus `dev` and `main`. No branch-per-feature — the overhead isn't worth it on this timeline.

### Every morning, before writing code

```bash
git checkout dev
git pull origin dev
git checkout feature/your-branch
git merge dev
```

Daily merges keep conflicts small. Skip a week and you get a nightmare.

### Every evening, before closing the laptop

```bash
git add .
git commit -m "Add ratio test to knapsack node evaluation"
git push origin feature/your-branch
```

Push even if it's unfinished. Uncommitted work on one laptop is work that doesn't exist.

### Merging

- Deliverable done → open a PR into `dev`. One other person reviews, merge same day.
- `dev` → `main` on **Day 6, Day 10, Day 14**. Person 1 runs these. `main` must always build.

### Commit messages

Short, specific, present tense.

- Good: `Add binary upper bound constraints to tableau`
- Bad: `stuff`, `fixed it`, `update`

### .csproj warning

`.csproj` files conflict constantly because Visual Studio rewrites them when you add files. If you add a new class file, **commit and push immediately** so everyone gets the change fast. Don't sit on it for two days.

---

## Schedule & gates

Fill in dates at the Day 1 meeting.

| Day | Date | Gate | Blocks |
|---|---|---|---|
| 1 | | Models, enums, interfaces on `main` | Everyone |
| 4 | | `Tableau` with pivot + snapshot logging | Persons 2, 3, 4 |
| 6 | | `CanonicalFormBuilder` | Persons 2, 3 |
| 7 | | Knapsack B&B complete | — |
| 8 | | `PrimalSimplex` solves `knapsack.txt` | Person 3 (Cutting Plane), Person 4 |
| 14 | | B&B Simplex, Cutting Plane, Sensitivity complete | — |
| 15 | | **Code freeze.** Full dry run of every criterion | — |
| 16–18 | | Video recording | — |
| 19 | | Buffer / submission | — |

**If a gate is going to slip, say so the same day.** A silent two-day slip on Day 4 costs the group six days downstream.

---

## Scope decisions

- **Non-linear bonus (10 marks): cut.** Only revisit if ahead on Day 15.
- Where the brief says "or", take the **non-revised** option — non-revised B&B Simplex, non-revised Cutting Plane. Same marks, less work.
- Revised *Primal* Simplex (4 marks) is separately required and cannot be skipped.

---

## AI use policy

**AI use is allowed.** Claude, Copilot, ChatGPT — use whatever helps.

**But marks come only from the video.** The lecturer is the client and will not read the code. You will be on camera explaining your algorithm. If you can't explain your own Branch and Bound tree, or why a particular row was chosen in the dual simplex ratio test, it shows instantly.

**The standard: you must be able to explain every line in your files.** Not "AI wrote it and it works." If AI gives you something you don't understand, ask it to explain until you do, or write it yourself.

**Readiness test before filming:** explain your algorithm out loud to one other group member, without notes. If you can't, you're not ready.

### Prompting rules

Paste the file structure above into your prompt and constrain the AI to your files:

> I own `Solver.Algorithms/KnapsackBranchAndBound.cs` and `KnapsackNode.cs`. Only produce code for those files. If you think something in `Solver.Core` needs changing, tell me instead of changing it.

Left unconstrained, AI will rewrite `LPModel` to suit its own solution and silently destroy Person 1's work.

**After any AI-assisted edit, run `git diff` before committing** and confirm nothing outside your own files changed.

---

## Mark allocation reference

| Criteria | Weight | Owner |
|---|---|---|
| Outline | 2 | Person 1 |
| Input File | 3 | Person 1 |
| Output File | 2 | Person 1 |
| Primal Simplex | 4 | Person 2 |
| Revised Primal Simplex | 4 | Person 2 |
| Branch & Bound Simplex | 20 | Person 2 |
| Branch & Bound Knapsack | 16 | Person 3 |
| Cutting Plane | 14 | Person 3 |
| Sensitivity Analysis | 25 | Person 4 |
| Error Handling / special cases | 5 | Persons 1 + 2 |
| Interface presentation | 5 | Person 1 |
| **Total** | **100** | |
