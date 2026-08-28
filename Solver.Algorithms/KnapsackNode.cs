// This file previously held a second, independent copy of the whole
// KnapsackBranchAndBound solver (a duplicate `KnapsackBranchAndBound` class in
// this namespace), left over from two branches implementing the same feature
// separately before merging. Both copies were verified against
// TestData/knapsack.txt and produced identical, correct results (z = 15,
// x2 = x3 = x4 = x6 = 1), so this is a style/ownership choice, not a
// correctness one.
//
// The single implementation now lives in KnapsackBranchAndBound.cs, which
// already defines its own private nested KnapsackNode and Item helper
// classes - nothing in this file was referenced from elsewhere in the
// project, so it is intentionally left empty rather than reintroducing a
// second public KnapsackNode type.
