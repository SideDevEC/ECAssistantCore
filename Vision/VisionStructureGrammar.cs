namespace ECAssistant.Core.Vision;

/// <summary>
/// GBNF grammar that force-constrains the vision model's output to the
/// VisionStructureResult v1.0 shape (server-side structured decoding).
/// Generated output can then only be valid-schema JSON — prompt adherence
/// becomes a physical guarantee. COMPACT by design: no whitespace rule —
/// a permissive ws rule lets the sampler degenerate into endless whitespace
/// runs at delimiter points (burns the token budget, never terminates).
/// Written to mirror DecisionGrammar conventions on the server.
/// Stateless utility — no mutable state.
/// </summary>
public static class VisionStructureGrammar
{
    public const string Gbnf = """
root ::= vobject
vobject ::= "{" "\"schemaVersion\"" ":" string "," "\"source\"" ":" source "," "\"elements\"" ":" "[" (element ("," element)*)? "]" "," "\"groups\"" ":" "[" (group ("," group)*)? "]" "," "\"warnings\"" ":" "[" (string ("," string)*)? "]" "}"
source ::= "{" "\"kind\"" ":" kind "," "\"page\"" ":" int "," "\"width\"" ":" int "," "\"height\"" ":" int "}"
element ::= "{" "\"id\"" ":" string "," "\"type\"" ":" etype "," "\"text\"" ":" string "," "\"bbox\"" ":" bbox "," "\"confidence\"" ":" number "," "\"associatedWith\"" ":" "[" (string ("," string)*)? "]" "}"
bbox ::= "{" "\"x\"" ":" int "," "\"y\"" ":" int "," "\"width\"" ":" int "," "\"height\"" ":" int "}"
group ::= "{" "\"id\"" ":" string "," "\"role\"" ":" grrole "," "\"memberIds\"" ":" "[" (string ("," string)*)? "]" "}"
kind ::= "\"screenshot\"" | "\"pdf-page\"" | "\"unknown\""
etype ::= "\"header\"" | "\"label\"" | "\"button\"" | "\"input\"" | "\"checkbox\"" | "\"radio\"" | "\"select\"" | "\"table\"" | "\"image\"" | "\"text\"" | "\"other\""
grrole ::= "\"form\"" | "\"section\"" | "\"toolbar\"" | "\"list\"" | "\"table\"" | "\"other\""
int ::= [0-9]+
number ::= int ("." [0-9]+)?
string ::= "\"" ( [^"\\] | "\\" ( ["\\bfnrt] | "u" [0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F] ) )* "\""
""";

    /// <summary>Grammar root rule name.</summary>
    public const string Root = "root";
}
