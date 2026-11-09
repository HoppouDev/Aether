# Mixin wrappers and startup IL editing

Add each mixin class to the `mixins` array in `aether.mod.json`.
The bootstrap applies changes before loading Terraria, so changing the patch set takes effect the next time the game starts.

For a given method, Aether resolves declarative instruction selectors against the original body, applies those injectors, runs its IL editors, then builds the whole-method wrapper chain.
An `[Overwrite]` conflicts with every other patch on that method.
See the [mixin authoring guide](modding.md#declare-a-mixin) for target selectors, remapping, and package setup.

Within each phase, lower `Order` runs first.
At equal order, higher mixin `Priority` runs first, followed by mod dependency order and stable handler and declaration order.
The default wrapper and editor order is `1000`.
In a wrapper chain the first wrapper is outermost, and results return through the chain in reverse order.

## Wrap a method

`[WrapMethod("Method(...)")]` wraps the entire invocation.
The handler takes a continuation delegate first, then the target arguments in order.
Its return type matches the target.
Calling `next` invokes the next wrapper or the edited game body.
The handler can pass different arguments, handle an exception from `next`, or return before invoking it.
Call `next` synchronously within the handler.

```csharp
using Aether.Modding;

[Mixin("Terraria.Main")]
public static class PauseMixin
{
    [WrapMethod("CanPauseGame():System.Boolean")]
    private static bool WrapCanPause(Func<bool> next)
    {
        bool canPause = next();
        return canPause && PauseSettings.MenuPauseEnabled;
    }
}
```

Use a static handler for a static target.
On an instance target, an instance handler's `this` is the game object.
A static handler is also allowed, and either handler gets a continuation already bound to the current instance.
The continuation takes just the method arguments.
Use `Func<...>` for ordinary return values and `Action<...>` for `void`, or use a public custom delegate with the exact signature.

Instruction injectors stay with the game body.
For example, a `RETURN` callback sees the result from that body before the wrappers change it.
If a wrapper skips `next`, the remaining wrappers and their game body do not run.
Recursive calls and calls dispatched through virtual methods use the original method entry point, so they pass through the wrapper chain again if they dispatch to the wrapped method.

### Generic and by-reference signatures

Use a custom delegate for `ref`, `out`, `in`, and ref returns.
Declare the delegate outside the mixin class, make it public, and use the same parameter and return types as the wrapped signature.
In CLR selectors, a by-ref type ends in `&`.
This example assumes a game method `ref int GetSlot(ref int index)`.

```csharp
using Aether.Modding;

public delegate ref int SlotContinuation(ref int index);

[Mixin("Example.GameType")]
public class SlotMixin
{
    [WrapMethod("GetSlot(System.Int32&)")]
    private ref int WrapSlot(SlotContinuation next, ref int index)
    {
        index = Math.Max(0, index);
        return ref next(ref index);
    }
}
```

For a generic game method, give the handler the same generic arity and CLR constraints.
Its generic parameter names can differ.
For example, select `Choose` with one generic parameter `T` and a parameter of that type using `` "Choose`1(T)" ``.
A matching unconstrained handler is `static TItem Choose<TItem>(Func<TItem, TItem> next, TItem item) => next(item);`.
The same rules apply to a wrapper inside a generic method.
Custom delegates in a private DLL must be resolvable from the mod's entry assembly directory.

Whole-method wrappers target methods with IL on non-generic game classes.
Constructors and varargs are excluded.
Use a constructor injection for work after the base or sibling constructor call, or wrap `NEW` to control object creation at a call site.

## Wrap an operation

`[WrapOperation(method, at, Target = ...)]` wraps one or more selected instructions.
The handler takes `next` first, then the instruction operands in evaluation order, and returns the instruction's result.
The delegate has the same operand and result signature as the instruction.
An instance handler can use the containing game method's `this`, independently of any explicit operation receiver.

| Point | Delegate operands and result |
| --- | --- |
| `At.INVOKE` | The receiver for an instance call, followed by the arguments. Returns the call's result or `void`. |
| `At.FIELD` read | The receiver for an instance field. Returns the field value. |
| `At.FIELD` write | The receiver for an instance field, then the value. Returns `void`. |
| `At.NEW` | The constructor arguments. Returns the new object. |

For an instance call on a value type, the receiver is passed by reference.
For a field on a value type, match the receiver on the evaluation stack.
A field read from a returned struct can take the receiver by value, while a field write requires a managed reference.
Aether rejects struct field sites when it cannot prove the receiver's stack form within the same basic block.
Use a custom delegate for by-ref operands or results.
A closed generic call can be selected with a signature such as `Example.Store::Find<System.Int32>(System.Int32)`.
For a call parameterized by its containing method, use its CLR generic parameter name in the selector, such as `Example.Store::Find<T>(T)`.

This example assumes an instance call to `Scale(int)` inside `UpdateScore()`.

```csharp
using Aether.Modding;

[Mixin(typeof(Scoreboard))]
public static class ScoreMixin
{
    [WrapOperation("UpdateScore()", At.INVOKE,
        Target = "Example.Scoreboard::Scale(System.Int32)", Ordinal = 0, Allow = 1)]
    private static int WrapScale(Func<Scoreboard, int, int> next, Scoreboard receiver, int score)
        => next(receiver, Math.Max(0, score));
}
```

`Ordinal`, `Slice`, `Require`, `Allow`, and `Expect` follow the [instruction selector rules](modding.md#select-methods-and-instructions).
Use unshifted sites.
At the same instruction, `ModifyArg` changes the operands before the wrappers run.
Multiple wrappers compose, and a redirect conflicts with a wrapper at the same instruction.
If the final continuation runs, it executes the original opcode, including virtual dispatch for `callvirt`.
Prefixed instructions, varargs, and writes to readonly fields are excluded.

## Edit IL at startup

Put `[EditIL("Method(...)")]` on a `static void` method taking `ILPatchContext`, in a dedicated `[Mixin]` class.
The class and its helpers execute in a separate load context during weaving.
Use a string target and keep editor code independent of live Terraria, FNA, ReLogic, and Aether API types.
The context can load the framework, `Aether.Modding`, `Mono.Cecil`, and the mod's private managed dependencies.
Editor state is separate from the mod initializer's state.

`ILPatchContext.Method` exposes the Cecil method and its module.
`Body` exposes instructions, locals, and exception handlers, and `IL` is the body’s `ILProcessor`.
Use these to match instruction sequences, insert or replace instructions, add locals, or change branches.
Edit the selected method body and use `Method.Module.ImportReference` for external references.
Any method called from the woven body must be accessible from Terraria and resolvable at runtime.

```csharp
using Aether.Modding;
using Mono.Cecil.Cil;

[Mixin("Example.Scoreboard")]
public static class ScoreEditor
{
    [EditIL("GetBonus():System.Int32")]
    private static void DoubleBonus(ILPatchContext patch)
    {
        var matches = patch.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Ldc_I4_5)
            .ToArray();

        if (matches.Length != 1)
            throw new InvalidOperationException($"Expected one bonus literal, found {matches.Length}.");

        matches[0].OpCode = OpCodes.Ldc_I4_S;
        matches[0].Operand = (sbyte)10;
    }
}
```

Each editor sees the result of previous edits.
Check every required pattern and report an actionable error if it changes.
When inserting or removing instructions, update any branch destinations and exception boundaries that should follow the change.
Aether checks for dangling references and validates control-flow stack depth.
The editor author is responsible for stack types, exception-region rules, and any other CLR validity requirements.
An editor failure aborts startup and names the mod, mixin, and target method in the [startup log](modding.md#diagnose-startup).
