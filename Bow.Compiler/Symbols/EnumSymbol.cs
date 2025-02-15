using System.Collections.Frozen;
using Bow.Compiler.Binding;
using Bow.Compiler.Diagnostics;
using Bow.Compiler.Syntax;

namespace Bow.Compiler.Symbols;

public sealed class EnumSymbol(ModuleSymbol module, EnumDefinitionSyntax syntax)
    : TypeSymbol,
        IItemSymbol
{
    private readonly DiagnosticBag _diagnosticBag = new();

    public override string Name => Syntax.Identifier.IdentifierText;
    public override EnumDefinitionSyntax Syntax { get; } = syntax;
    public override ModuleSymbol Module { get; } = module;

    private EnumBinder? _lazyBinder;
    internal override EnumBinder Binder => _lazyBinder ??= new(this);

    public override SymbolAccessibility Accessibility =>
        SymbolFacts.GetAccessibilityFromToken(Syntax.AccessModifier, SymbolAccessibility.File);

    private ImmutableArray<EnumCaseSymbol> _lazyCases;
    public ImmutableArray<EnumCaseSymbol> Cases =>
        _lazyCases.IsDefault ? _lazyCases = CreateCases() : _lazyCases;

    private ImmutableArray<MethodSymbol> _lazyMethods;
    public ImmutableArray<MethodSymbol> Methods =>
        _lazyMethods.IsDefault ? _lazyMethods = CreateMethods() : _lazyMethods;

    private FrozenDictionary<string, Symbol>? _lazyMembers;
    public FrozenDictionary<string, Symbol> MemberMap => _lazyMembers ??= CreateMemberMap();

    private ImmutableArray<Diagnostic> _lazyDiagnostics;
    public ImmutableArray<Diagnostic> Diagnostics =>
        _lazyDiagnostics.IsDefault
            ? _lazyDiagnostics = _diagnosticBag.ToImmutableArray()
            : _lazyDiagnostics;

    ItemSyntax IItemSymbol.Syntax => Syntax;

    private ImmutableArray<EnumCaseSymbol> CreateCases()
    {
        var builder = ImmutableArray.CreateBuilder<EnumCaseSymbol>(Syntax.Cases.Count);
        foreach (var syntax in Syntax.Cases)
        {
            var argumentType =
                syntax.Argument == null
                    ? null
                    : Binder.BindType(syntax.Argument.TypeReference, _diagnosticBag);

            EnumCaseSymbol @case = new(this, syntax, argumentType);
            builder.Add(@case);
        }

        return builder.MoveToImmutable();
    }

    private ImmutableArray<MethodSymbol> CreateMethods()
    {
        var builder = ImmutableArray.CreateBuilder<MethodSymbol>(Syntax.Methods.Count);
        foreach (var syntax in Syntax.Methods)
        {
            var returnType =
                syntax.ReturnType == null
                    ? BuiltInModule.Unit
                    : Binder.BindType(syntax.ReturnType, _diagnosticBag);

            MethodSymbol method = new(this, syntax, returnType);
            builder.Add(method);
        }

        return builder.MoveToImmutable();
    }

    private IEnumerable<Symbol> GetOrderedMembers()
    {
        var slot = 0;
        foreach (var @case in Cases)
        {
            if (@case.Syntax.Slot == slot)
            {
                slot++;
                yield return @case;
            }
        }

        foreach (var method in Methods)
        {
            if (method.Syntax.Slot == slot)
            {
                slot++;
                yield return method;
            }
        }
    }

    private FrozenDictionary<string, Symbol> CreateMemberMap()
    {
        Dictionary<string, Symbol> map = new(Cases.Length + Methods.Length);
        foreach (var member in GetOrderedMembers())
        {
            if (map.TryAdd(member.Name, member))
            {
                continue;
            }

            var identifier = member.Syntax switch
            {
                EnumCaseDeclarationSyntax s => s.Identifier,
                FunctionDefinitionSyntax s => s.Identifier,
                _ => throw new UnreachableException()
            };

            _diagnosticBag.AddError(
                identifier,
                DiagnosticMessages.NameIsAlreadyDefined,
                member.Name
            );
        }

        return map.ToFrozenDictionary();
    }
}
