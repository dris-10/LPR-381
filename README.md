LPR381Project/
├── LPR381Project.sln
│
├── Solver.Core/                    ← OWNER: Person 1
│   ├── Models/
│   │   ├── LPModel.cs
│   │   ├── Constraint.cs
│   │   ├── SignRestriction.cs      (enum)
│   │   ├── RelationType.cs         (enum)
│   │   └── ObjectiveType.cs        (enum)
│   ├── Tableau/
│   │   ├── Tableau.cs
│   │   ├── TableauSnapshot.cs
│   │   └── PivotOperation.cs
│   ├── Results/
│   │   ├── SolutionResult.cs
│   │   ├── SolutionStatus.cs       (enum)
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
