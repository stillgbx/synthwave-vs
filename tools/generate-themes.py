"""Apply the editor palettes to both theme variants.

Both variants share every environment color, only the text editor colors differ:
* SynthWave '84: the original token colors of https://github.com/robb0wen/synthwave-vscode
* Neon Dream: the colors of its "Neon Dreams" mode (synthwave84.css). The extension draws
  the glow around these colors, and only in themes that define the NeonGlow marker.

NeonDream.vstheme is the source: edit environment colors there, then run
    python tools/generate-themes.py
It recolors NeonDream.vstheme in place and regenerates SynthWave84.vstheme from it.
"""
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
NEON_DREAM = ROOT / 'NeonDream.vstheme'
SYNTHWAVE = ROOT / 'SynthWave84.vstheme'

SYNTHWAVE_THEME = ("SynthWave '84", '{c4f61b06-eac9-437e-bbc8-631691332d1b}')

# Text editor categories (Fonts and Colors > Text Editor)
MEF_ITEMS = '{75a05685-00a8-4ded-bae5-e7a50bfa929a}'
LANGUAGE_SERVICE_ITEMS = '{e0187991-b458-4f7e-8ca9-42c9a573b56c}'

# Marker read by the glow (Glow/GlowTheme.cs): only themes defining it glow.
GLOW_CATEGORY = '{cab52692-d5e4-4f3a-a97a-10fbb0b9859f}'
GLOW_MARKER = (
    f'    <Category Name="SynthWave" GUID="{GLOW_CATEGORY}">\n'
    '      <Color Name="NeonGlow">\n'
    '        <Background Type="CT_RAW" Source="FFFFFFFF" />\n'
    '      </Color>\n'
    '    </Category>\n')

# Like in VS Code, only a few targeted roles glow in Neon Dream: keywords, types, functions,
# tags and, faintly, variables. Their colors are the keys of Glow/GlowPalette.cs; the other
# roles must keep colors outside that palette.
#                 SynthWave '84   Neon Dream
PALETTES = {
    'keyword':   ('FFFEDE5D',     'FFF4EEE4'),  # glows orange
    'type':      ('FFFE4450',     'FFFFF5F6'),  # glows red
    'function':  ('FF36F9F6',     'FFFDFDFD'),  # glows cyan
    'tag':       ('FF72F1B8',     'FF72F1B8'),  # glows green
    'variable':  ('FFFF7EDB',     'FFF92AAD'),  # faint pink glow
    'operator':  ('FFFEDE5D',     'FFFEDE5D'),
    'attribute': ('FFFEDE5D',     'FFFEDE5D'),
    'string':    ('FFFF8B39',     'FFF97E72'),
    'constant':  ('FFF97E72',     'FFF97E72'),
    'comment':   ('FF848BBD',     'FF6D77B3'),
    'escape':    ('FF36F9F6',     'FF36F9F6'),
}

ROLES = {
    'keyword': [
        'Keyword', 'keyword - control',
        'CppControlKeywordSyntacticTokenFormat', 'CppNewDeleteSemanticTokenFormat', 'C/C++ User Keywords',
        'CSS Keyword',
    ],
    'operator': [
        'Operator', 'operator - overloaded', 'CppOperatorSemanticTokenFormat', 'CppMemberOperatorSemanticTokenFormat',
    ],
    'attribute': [
        'HTML Attribute Name', 'HTML Attribute', 'Markup Attribute', 'XML Attribute', 'XAML Attribute',
    ],
    'string': [
        'String', 'string - verbatim', 'CSS String Value', 'VBScript String', 'CppStringDelimiterCharacterSyntacticTokenFormat',
        'HTML Attribute Value', 'Markup Attribute Value', 'XML Attribute Value', 'XAML Attribute Value',
        'XML Attribute Quotes', 'XAML Attribute Quotes',
    ],
    'constant': [
        'Number', 'VBScript Number', 'CppUDLNumberSemanticTokenFormat', 'constant name', 'enum member name',
        'CSS Property Value',
    ],
    'comment': [
        'Comment', 'CSS Comment', 'HTML Comment', 'VBScript Comment', 'XAML Comment', 'XML Comment',
        'XML Doc Comment', 'XML Doc Tag', 'regex - comment',
        'xml doc comment - attribute name', 'xml doc comment - attribute quotes', 'xml doc comment - attribute value',
        'xml doc comment - cdata section', 'xml doc comment - comment', 'xml doc comment - delimiter',
        'xml doc comment - entity reference', 'xml doc comment - name', 'xml doc comment - processing instruction',
        'xml doc comment - text',
    ],
    'type': [
        'Type', 'class name', 'record class name', 'struct name', 'record struct name', 'interface name', 'enum name',
        'delegate name', 'module name', 'type parameter name', 'namespace name',
        'CppTypeSemanticTokenFormat', 'CppClassTemplateSemanticTokenFormat', 'CppGenericTypeSemanticTokenFormat',
        'CppRefTypeSemanticTokenFormat', 'CppValueTypeSemanticTokenFormat', 'CppEnumSemanticTokenFormat',
        'CppNamespaceSemanticTokenFormat',
    ],
    'function': [
        'method name', 'extension method name',
        'CppFunctionSemanticTokenFormat', 'CppFunctionTemplateSemanticTokenFormat',
        'CppMemberFunctionSemanticTokenFormat', 'CppStaticMemberFunctionSemanticTokenFormat',
    ],
    'escape': [
        'string - escape character', 'CppStringEscapeCharacterSyntacticTokenFormat',
        'HTML Tag Delimiter', 'XML Delimiter', 'XAML Delimiter', 'xml literal - delimiter',
    ],
    'variable': [
        'Identifier', 'local name', 'parameter name', 'field name', 'property name', 'event name',
        'CppLocalVariableSemanticTokenFormat', 'CppParameterSemanticTokenFormat', 'CppMemberFieldSemanticTokenFormat',
        'CppGlobalVariableSemanticTokenFormat', 'CppStaticMemberFieldSemanticTokenFormat',
        'CppPropertySemanticTokenFormat', 'CppEventSemanticTokenFormat',
    ],
    'tag': [
        'Preprocessor Keyword', 'CppMacroSemanticTokenFormat',
        'HTML Element Name', 'Markup Node', 'XML Name', 'XAML Name', 'xml literal - name',
        'CSS Selector', 'CSS Property Name', 'JSON Property Name',
    ],
}

CATEGORY = re.compile(r'(    <Category Name="[^"]*" GUID="(\{[^}]+\})">\r?\n)(.*?)(    </Category>\r?\n)', re.S)
COLOR = re.compile(r'      <Color Name="([^"]*)">\r?\n.*?      </Color>\r?\n', re.S)
FOREGROUND = re.compile(r'<Foreground Type="[^"]*" Source="[^"]*" />')


def foreground(color):
    return f'<Foreground Type="CT_RAW" Source="{color}" />'


def recolor(text, variant):
    """Set the editor colors of one variant (0 = SynthWave '84, 1 = Neon Dream)."""
    nl = '\r\n' if '\r\n' in text else '\n'
    colors = {name: PALETTES[role][variant] for role, names in ROLES.items() for name in names}
    in_mef = set()

    def recolor_category(m):
        header, guid, body, footer = m.groups()
        guid = guid.lower()
        if guid not in (MEF_ITEMS, LANGUAGE_SERVICE_ITEMS):
            return m.group(0)

        def recolor_color(cm):
            name = cm.group(1)
            if name not in colors:
                return cm.group(0)
            if guid == MEF_ITEMS:
                in_mef.add(name)
            block = cm.group(0)
            if FOREGROUND.search(block):
                return FOREGROUND.sub(foreground(colors[name]), block, count=1)
            return block.replace('      </Color>', f'        {foreground(colors[name])}{nl}      </Color>')

        return header + COLOR.sub(recolor_color, body) + footer

    text = CATEGORY.sub(recolor_category, text)

    # VS 2026 reads the semantic colors from the MEF category: add the ones it does not have yet.
    added = ''.join(
        f'      <Color Name="{n}">{nl}'
        f'        <Background Type="CT_AUTOMATIC" Source="00000000" />{nl}'
        f'        {foreground(c)}{nl}'
        f'      </Color>{nl}' for n, c in colors.items() if n not in in_mef)
    mef = next(m for m in CATEGORY.finditer(text) if m.group(2).lower() == MEF_ITEMS)
    return text[:mef.start(4)] + added + text[mef.start(4):]


def without_glow_marker(text):
    return CATEGORY.sub(lambda m: '' if m.group(2).lower() == GLOW_CATEGORY else m.group(0), text)


def with_glow_marker(text):
    nl = '\r\n' if '\r\n' in text else '\n'
    text = without_glow_marker(text)
    i = text.rindex('  </Theme>')
    return text[:i] + GLOW_MARKER.replace('\n', nl) + text[i:]


def main():
    source = NEON_DREAM.read_text(encoding='utf-8-sig')

    neon = with_glow_marker(recolor(source, 1))
    NEON_DREAM.write_text(neon, encoding='utf-8-sig', newline='')

    name, guid = SYNTHWAVE_THEME
    synthwave, count = re.subn(r'<Theme Name="[^"]*" GUID="\{[^}]+\}"', f'<Theme Name="{name}" GUID="{guid}"', neon, count=1)
    assert count == 1
    SYNTHWAVE.write_text(without_glow_marker(recolor(synthwave, 0)), encoding='utf-8-sig', newline='')

    print(f'{NEON_DREAM.name} and {SYNTHWAVE.name} generated')


if __name__ == '__main__':
    main()
