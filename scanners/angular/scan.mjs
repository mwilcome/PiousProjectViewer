import fs from "fs";
import path from "path";
import ts from "typescript";

const folder = process.argv[2];
if (!folder) {
  console.error("missing folder");
  process.exit(2);
}

const sourceRoot = fs.existsSync(path.join(folder, "src")) ? path.join(folder, "src") : folder;
const files = walk(sourceRoot);
const tsFiles = files.filter((file) => file.endsWith(".ts") && !file.endsWith(".spec.ts") && !file.endsWith(".d.ts"));
const types = [];

for (const file of tsFiles) {
  const text = fs.readFileSync(file, "utf8");
  const sf = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
  const space = namespaceOf(file);
  const classes = [];
  visit(sf, (node) => {
    if (!ts.isClassDeclaration(node) || !node.name) return;
    classes.push(readClass(node, sf, file, text));
  });
  const host = classes.find((item) => item.role) ?? classes[0];
  if (host) attachParts(host, file);
  for (const item of classes) {
    item.space = space;
    item.usesAngular = text.includes("@angular/");
    types.push(item);
  }
}

process.stdout.write(JSON.stringify({ types }));

function readClass(node, sf, file, text) {
  const role = roleOf(node);
  const members = [];
  const injected = [];
  for (const member of node.members) {
    if (ts.isConstructorDeclaration(member)) {
      members.push(methodMember("constructor()", member, sf, true));
      for (const param of member.parameters) noteInjected(injected, param.type, param.initializer);
    } else if (ts.isMethodDeclaration(member) && member.name && ts.isIdentifier(member.name)) {
      members.push(methodMember(member.name.text + "()", member, sf, !hasModifier(member, ts.SyntaxKind.PrivateKeyword) && !hasModifier(member, ts.SyntaxKind.ProtectedKeyword)));
    } else if ((ts.isPropertyDeclaration(member) || ts.isGetAccessor(member) || ts.isSetAccessor(member)) && member.name && ts.isIdentifier(member.name)) {
      if (ts.isPropertyDeclaration(member)) noteInjected(injected, null, member.initializer);
      members.push({
        name: member.name.text,
        line: lineOf(member, sf),
        cc: 0,
        kind: "field",
        isPublic: !hasModifier(member, ts.SyntaxKind.PrivateKeyword) && !hasModifier(member, ts.SyntaxKind.ProtectedKeyword),
        file
      });
    }
  }
  const imports = [];
  sf.forEachChild((statement) => {
    if (!ts.isImportDeclaration(statement) || !statement.importClause?.namedBindings || !ts.isNamedImports(statement.importClause.namedBindings)) return;
    for (const element of statement.importClause.namedBindings.elements) imports.push(element.name.text);
  });
  return {
    name: node.name.text,
    file,
    line: lineOf(node, sf),
    role,
    selector: selectorOf(node),
    injected,
    imports,
    members
  };
}

function noteInjected(names, typeNode, initializer) {
  const fromType = typeRefName(typeNode);
  if (fromType && !names.includes(fromType)) names.push(fromType);
  const fromCall = injectName(initializer);
  if (fromCall && !names.includes(fromCall)) names.push(fromCall);
}

function typeRefName(typeNode) {
  if (!typeNode || !ts.isTypeReferenceNode(typeNode) || !ts.isIdentifier(typeNode.typeName)) return "";
  return typeNode.typeName.text;
}

function injectName(expr) {
  if (!expr || !ts.isCallExpression(expr) || !ts.isIdentifier(expr.expression) || expr.expression.text !== "inject") return "";
  const arg = expr.arguments[0];
  return arg && ts.isIdentifier(arg) ? arg.text : "";
}

function selectorOf(node) {
  const decorators = ts.canHaveDecorators(node) ? ts.getDecorators(node) : undefined;
  if (!decorators) return "";
  for (const decorator of decorators) {
    if (!ts.isCallExpression(decorator.expression)) continue;
    const arg = decorator.expression.arguments[0];
    if (!arg || !ts.isObjectLiteralExpression(arg)) continue;
    for (const prop of arg.properties) {
      if (!ts.isPropertyAssignment(prop) || prop.name.getText() !== "selector") continue;
      if (ts.isStringLiteral(prop.initializer) || ts.isNoSubstitutionTemplateLiteral(prop.initializer))
        return prop.initializer.text;
    }
  }
  return "";
}

function methodMember(name, node, sf, isPublic) {
  return {
    name,
    line: lineOf(node, sf),
    cc: complexity(node),
    kind: "method",
    isPublic,
    file: sf.fileName
  };
}

function complexity(node) {
  let score = 1;
  function visit(child) {
    if (ts.isIfStatement(child) || ts.isWhileStatement(child) || ts.isForStatement(child) || ts.isForInStatement(child) || ts.isForOfStatement(child) || ts.isDoStatement(child) || ts.isCatchClause(child) || ts.isConditionalExpression(child) || ts.isCaseClause(child))
      score++;
    if (ts.isBinaryExpression(child)) {
      const kind = child.operatorToken.kind;
      if (kind === ts.SyntaxKind.AmpersandAmpersandToken || kind === ts.SyntaxKind.BarBarToken || kind === ts.SyntaxKind.QuestionQuestionToken)
        score++;
    }
    ts.forEachChild(child, visit);
  }
  visit(node);
  return score;
}

function attachParts(type, tsFile) {
  const dir = path.dirname(tsFile);
  const base = path.basename(tsFile, ".ts");
  const template = decoratorFile(tsFile, "templateUrl") ?? sibling(dir, base, [".html", ".component.html"]);
  const style = decoratorFile(tsFile, "styleUrl") ?? sibling(dir, base, [".scss", ".css", ".component.scss", ".component.css"]);
  if (template) type.members.push({ name: "template", line: 1, cc: 0, kind: "html", isPublic: true, file: template });
  if (style) type.members.push({ name: "styles", line: 1, cc: 0, kind: "scss", isPublic: true, file: style });
}

function sibling(dir, base, extensions) {
  for (const ext of extensions) {
    const candidate = path.join(dir, base + ext);
    if (fs.existsSync(candidate)) return candidate;
  }
  return null;
}

function decoratorFile(tsFile, property) {
  const text = fs.readFileSync(tsFile, "utf8");
  const match = text.match(new RegExp(property + "\\s*:\\s*['\"]([^'\"]+)['\"]"));
  if (!match) return null;
  const resolved = path.resolve(path.dirname(tsFile), match[1]);
  return fs.existsSync(resolved) ? resolved : null;
}

function roleOf(node) {
  const decorators = ts.canHaveDecorators(node) ? ts.getDecorators(node) : undefined;
  if (!decorators) return "";
  for (const decorator of decorators) {
    const expr = ts.isCallExpression(decorator.expression) ? decorator.expression.expression : decorator.expression;
    const name = expr.getText();
    if (["Component", "Injectable", "NgModule", "Directive", "Pipe"].includes(name)) return name.toLowerCase();
  }
  return "";
}

function hasModifier(node, kind) {
  return (ts.canHaveModifiers(node) ? ts.getModifiers(node) : undefined)?.some((modifier) => modifier.kind === kind) ?? false;
}

function lineOf(node, sf) {
  return sf.getLineAndCharacterOfPosition(node.getStart(sf)).line + 1;
}

function namespaceOf(file) {
  const rel = path.relative(sourceRoot, path.dirname(file));
  if (!rel || rel === ".") return "app";
  return rel.split(path.sep).join(".");
}

function visit(node, fn) {
  fn(node);
  ts.forEachChild(node, (child) => visit(child, fn));
}

function walk(dir, acc = []) {
  if (!fs.existsSync(dir)) return acc;
  for (const name of fs.readdirSync(dir)) {
    if (["node_modules", "dist", ".angular", "coverage"].includes(name)) continue;
    const full = path.join(dir, name);
    let stat;
    try { stat = fs.statSync(full); } catch { continue; }
    if (stat.isDirectory()) walk(full, acc);
    else acc.push(full);
  }
  return acc;
}
