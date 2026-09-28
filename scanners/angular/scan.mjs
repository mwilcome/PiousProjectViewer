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
const types = [];

for (const file of files) {
  if (!isSource(file)) continue;
  if (file.endsWith(".vue") || file.endsWith(".svelte")) {
    readSfc(file);
    continue;
  }
  const text = fs.readFileSync(file, "utf8");
  const kind = isJsxFile(file) ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
  const sf = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, kind);
  const space = namespaceOf(file);
  const classes = [];
  visit(sf, (node) => {
    if (!ts.isClassDeclaration(node) || !node.name) return;
    classes.push(readClass(node, sf, file, text));
  });
  const functions = readFunctionComponents(sf, file);
  const host = classes.find((item) => item.role) ?? classes[0];
  if (host) attachParts(host, file);
  for (const item of functions) attachParts(item, file);
  for (const item of [...classes, ...functions]) {
    item.space = space;
    item.usesAngular = text.includes("@angular/");
    types.push(item);
  }
}

process.stdout.write(JSON.stringify({ types }));

function isSource(file) {
  if (file.endsWith(".d.ts")) return false;
  if (/\.(spec|test)\.(ts|tsx|js|jsx)$/.test(file)) return false;
  return [".ts", ".tsx", ".js", ".jsx", ".vue", ".svelte"].some((ext) => file.endsWith(ext));
}

function isJsxFile(file) {
  return file.endsWith(".tsx") || file.endsWith(".jsx");
}

function readSfc(file) {
  const text = fs.readFileSync(file, "utf8");
  const parts = splitSfc(text, file.endsWith(".vue") ? "vue" : "svelte");
  const script = parts.script.trim() ? parts.script : "export {}";
  const kind = script.includes("<") ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
  const sf = ts.createSourceFile(file, script, ts.ScriptTarget.Latest, true, kind);
  const space = namespaceOf(file);
  const classes = [];
  visit(sf, (node) => {
    if (!ts.isClassDeclaration(node) || !node.name) return;
    classes.push(readClass(node, sf, file, script));
  });
  const functions = readFunctionComponents(sf, file);
  const stem = path.basename(file, path.extname(file));
  let host = classes.find((item) => item.role) ?? functions[0] ?? classes[0];
  if (!host) {
    host = {
      name: stem,
      file,
      line: 1,
      role: "component",
      selector: stem,
      injected: [],
      imports: collectImports(sf),
      members: []
    };
  }
  host.role = host.role || "component";
  if (!host.selector) host.selector = host.name;
  pushMember(host, "template", "html", file);
  if (parts.style.trim()) pushMember(host, "styles", "scss", file);
  host.space = space;
  host.usesAngular = script.includes("@angular/");
  types.push(host);
  for (const extra of [...classes, ...functions]) {
    if (extra === host) continue;
    extra.space = space;
    extra.usesAngular = host.usesAngular;
    types.push(extra);
  }
}

function splitSfc(text, kind) {
  const script = text.match(/<script\b[^>]*>([\s\S]*?)<\/script>/i);
  const style = [...text.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/gi)].map((match) => match[1]).join("\n");
  const template = kind === "vue"
    ? text.match(/<template\b[^>]*>([\s\S]*?)<\/template>/i)?.[1] ?? ""
    : text.replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, " ").replace(/<style\b[^>]*>[\s\S]*?<\/style>/gi, " ");
  return { script: script?.[1] ?? "", style, template };
}

function readFunctionComponents(sf, file) {
  const found = [];
  const imports = collectImports(sf);
  sf.forEachChild((statement) => {
    if (ts.isFunctionDeclaration(statement) && statement.name && isExported(statement) && isComponent(statement.name.text, statement, file))
      found.push(functionType(statement.name.text, statement, sf, file, imports));
    if (!ts.isVariableStatement(statement) || !isExported(statement)) return;
    for (const decl of statement.declarationList.declarations) {
      if (!ts.isIdentifier(decl.name) || !decl.initializer) continue;
      const init = decl.initializer;
      if (!(ts.isArrowFunction(init) || ts.isFunctionExpression(init))) continue;
      if (!isComponent(decl.name.text, init, file)) continue;
      found.push(functionType(decl.name.text, init, sf, file, imports, decl));
    }
  });
  return found;
}

function isComponent(name, node, file) {
  return /^[A-Z]/.test(name) && (isJsxFile(file) || hasJsx(node));
}

function functionType(name, node, sf, file, imports, at) {
  return {
    name,
    file,
    line: lineOf(at ?? node, sf),
    role: "component",
    selector: name,
    injected: [],
    imports,
    members: [methodMember(name + "()", node, sf, true)]
  };
}

function hasJsx(node) {
  let found = false;
  visit(node, (child) => {
    if (ts.isJsxElement(child) || ts.isJsxSelfClosingElement(child) || ts.isJsxFragment(child)) found = true;
  });
  return found;
}

function isExported(node) {
  return (ts.canHaveModifiers(node) ? ts.getModifiers(node) : undefined)?.some((modifier) => modifier.kind === ts.SyntaxKind.ExportKeyword) ?? false;
}

function collectImports(sf) {
  const imports = [];
  sf.forEachChild((statement) => {
    if (!ts.isImportDeclaration(statement) || !statement.importClause) return;
    if (statement.importClause.name) imports.push(statement.importClause.name.text);
    const named = statement.importClause.namedBindings;
    if (named && ts.isNamedImports(named)) {
      for (const element of named.elements) imports.push(element.name.text);
    }
  });
  return imports;
}

function pushMember(type, name, kind, file) {
  type.members.push({ name, line: 1, cc: 0, kind, isPublic: true, file });
}

function readClass(node, sf, file, text) {
  let role = roleOf(node);
  let selector = selectorOf(node);
  if (!role && isJsxFile(file) && /^[A-Z]/.test(node.name.text)) role = "component";
  if (role === "component" && !selector) selector = node.name.text;
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
  return {
    name: node.name.text,
    file,
    line: lineOf(node, sf),
    role,
    selector,
    injected,
    imports: collectImports(sf),
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
  if (tsFile.endsWith(".vue") || tsFile.endsWith(".svelte")) return;
  if (isJsxFile(tsFile)) {
    pushMember(type, "template", "html", tsFile);
    const style = sibling(path.dirname(tsFile), path.basename(tsFile).replace(/\.(tsx|jsx)$/, ""), [".module.scss", ".module.css", ".scss", ".css"]);
    if (style) pushMember(type, "styles", "scss", style);
    return;
  }
  const dir = path.dirname(tsFile);
  const base = path.basename(tsFile, path.extname(tsFile));
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
  let names;
  try { names = fs.readdirSync(dir); } catch { return acc; }
  for (const name of names) {
    if (["node_modules", "dist", ".angular", "coverage", ".next", ".svelte-kit", "bin", "obj"].includes(name)) continue;
    const full = path.join(dir, name);
    let stat;
    try { stat = fs.statSync(full); } catch { continue; }
    if (stat.isDirectory()) walk(full, acc);
    else acc.push(full);
  }
  return acc;
}
