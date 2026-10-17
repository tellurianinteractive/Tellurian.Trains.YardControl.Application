# Regenerates the Stream Deck key icons from one set of constants.
#
# The point of generating rather than hand-editing: EBICOS gives a SMALL pierced triangle
# its own meaning (the stored route start point, SignalView.StartPointFlag), so every main
# signal on every key must be drawn at one size. Hand-patching twenty files had already
# produced four different sizes.
import io, os, sys

OUT = sys.argv[1]

GREY, GREEN, YELLOW, RED = "#969696", "#008000", "#FFFF00", "#FF0000"
MAGENTA, BLUE, WHITE, DIM = "#FF3399", "#3366FF", "#FFFFFF", "#4A4A4A"

# One size for every main signal and dwarf chevron, everywhere. Capped by the route icons,
# which must hold two of them plus the link between within 96 units.
S = 34.0
H, HI, HL, HH = S / 2, S / 4, S * 0.6, S * 0.175       # half height, hole inset/length/half height
ND, NH = S * 0.4375, S * 0.21875                        # chevron notch depth / half height
BREAK = 8.0                                             # track stops this far short of a symbol
TRACK, THIN = 10.0, 9.0


def n(v):
    return f"{v:g}"


def tri(tip, cy, colour):
    """Main signal: solid triangle with a triangular hole, SignalView's own ratios."""
    b = tip - S
    return (f'<path d="M{n(b)},{n(cy-H)} L{n(tip)},{n(cy)} L{n(b)},{n(cy+H)} Z '
            f'M{n(b+HI)},{n(cy-HH)} L{n(b+HL)},{n(cy)} L{n(b+HI)},{n(cy+HH)} Z" '
            f'fill-rule="evenodd" fill="{colour}"/>')


def chev(b, cy, colour):
    """Dwarf signal: arrowhead with a V-notch cut into its back."""
    return (f'<polygon points="{n(b)},{n(cy-H)} {n(b+S)},{n(cy)} {n(b)},{n(cy+H)} '
            f'{n(b)},{n(cy+NH)} {n(b+ND)},{n(cy)} {n(b)},{n(cy-NH)}" fill="{colour}"/>')


def line(x1, y1, x2, y2, colour, w=TRACK):
    return (f'<line x1="{n(x1)}" y1="{n(y1)}" x2="{n(x2)}" y2="{n(y2)}" '
            f'stroke="{colour}" stroke-width="{n(w)}" stroke-linecap="round"/>')


# Padlock, top right. Smaller than before so a full-size signal fits beside it.
# Magenta shut = the interlocking is holding it; grey open = not held.
def padlock(shut):
    colour = MAGENTA if shut else GREY
    arc = ('M71,24 V18 a8 8 0 0 1 16 0 V24' if shut else 'M61,28 V16 a8 8 0 0 1 16 0 V24')
    return (f'<path d="{arc}" fill="none" stroke="{colour}" stroke-width="4.5" stroke-linecap="round"/>'
            f'<path d="M70,24 H88 a4 4 0 0 1 4 4 V42 a4 4 0 0 1 -4 4 H70 a4 4 0 0 1 -4 -4 V28 '
            f'a4 4 0 0 1 4 -4 Z M79,32 m-3,0 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0 Z '
            f'M77.5,34 H80.5 V42 H77.5 Z" fill-rule="evenodd" fill="{colour}"/>')


def small_point():
    return line(4, 76, 58, 76, GREY, THIN) + line(28, 76, 58, 58, GREY, THIN)


def fan(colour):
    return (f'<g stroke="{colour}" stroke-width="8" stroke-linecap="round">'
            '<line x1="4" y1="86" x2="42" y2="86"/><line x1="16" y1="86" x2="40" y2="68"/>'
            '<line x1="56" y1="86" x2="92" y2="86"/><line x1="68" y1="86" x2="90" y2="68"/></g>')


# --- routes: two signals of the one size, the link between them coloured ---------------
FROM_TIP, TO_TIP = 36.0, 94.0
BAR_A, BAR_B = FROM_TIP + 4, TO_TIP - S - 4


def route(shape, bar, frm, to):
    a = (tri(FROM_TIP, 52, frm) + tri(TO_TIP, 52, to)) if shape == "tri" \
        else (chev(FROM_TIP - S, 52, frm) + chev(TO_TIP - S, 52, to))
    return line(BAR_A, 52, BAR_B, 52, bar) + a


# --- train lifecycle: one signal, and the number moving past it -------------------------
LIFE_TIP, LIFE_CY = 65.0, 70.0


def life(sig, num_x, num_colour, tick_x, arrow):
    s = line(4, LIFE_CY, LIFE_TIP - S - BREAK, LIFE_CY, GREY)
    s += line(LIFE_TIP + BREAK, LIFE_CY, 92, LIFE_CY, GREY)
    s += tri(LIFE_TIP, LIFE_CY, sig)
    if tick_x:
        s += f'<line x1="{n(tick_x)}" y1="52" x2="{n(tick_x)}" y2="88" stroke="{GREY}" stroke-width="4"/>'
    s += (f'<text x="{n(num_x)}" y="42" text-anchor="middle" font-family="Consolas, monospace" '
          f'font-size="15" font-weight="bold" fill="{num_colour}">2145</text>')
    if arrow == "in":
        s += f'<path d="M4,14 L18,22 L4,30 Z" fill="{GREY}"/>'
    if arrow == "out":
        s += f'<path d="M78,14 L92,22 L78,30 Z" fill="{GREY}"/>'
    return s


def cards(front, back):
    def card(x, y, c):
        return (f'<rect x="{x}" y="{y}" width="44" height="44" rx="8" fill="none" stroke="{c}" stroke-width="4"/>'
                f'<g fill="{c}"><rect x="{x+5}" y="{y+5}" width="13" height="13" rx="2"/>'
                f'<rect x="{x+26}" y="{y+5}" width="13" height="13" rx="2"/>'
                f'<rect x="{x+5}" y="{y+26}" width="13" height="13" rx="2"/>'
                f'<rect x="{x+26}" y="{y+26}" width="13" height="13" rx="2"/></g>')
    return (card(4, 46, front) + card(48, 6, back)
            + f'<line x1="62" y1="84" x2="74" y2="72" stroke="{back}" stroke-width="5" stroke-linecap="round"/>'
            + f'<polygon points="84,62 79.8,74.8 71.2,66.2" fill="{back}"/>')


# Standalone signal: stub, gap, symbol, centred as one assembly.
SIG_STUB_A = 11.0
SIG_TIP = SIG_STUB_A + 32 + BREAK + S

ICONS = {
    "point-straight": ("Point straight",
        "Blade lifted clear of the through track: the point is set straight.",
        line(54, 46, 88, 26, DIM) + line(6, 64, 90, 64, GREY)),
    "point-diverging": ("Point diverging",
        "Blade laid in; the arm turned away is dim.",
        line(52, 64, 90, 64, DIM) + line(6, 64, 42, 64, GREY) + line(42, 64, 88, 26, GREY)),
    "signal-stop": ("Signal to stop",
        "Pierced triangle (6.2.3), grey for stop.",
        line(SIG_STUB_A, 50, SIG_STUB_A + 32, 50, GREY) + tri(SIG_TIP, 50, GREY)),
    "signal-go": ("Signal to go",
        "The same symbol at the same size, in route green.",
        line(SIG_STUB_A, 50, SIG_STUB_A + 32, 50, GREY) + tri(SIG_TIP, 50, GREEN)),
    "route-train-set": ("Train route set",
        "From signal green, destination red - what YardView draws.",
        route("tri", GREEN, GREEN, RED)),
    "route-train-cancel": ("Train route cancel",
        "Magenta throughout: a route being released.",
        route("tri", MAGENTA, MAGENTA, MAGENTA)),
    "route-shunting-set": ("Shunting route set",
        "Dwarf chevrons (6.2.4) over a yellow route.",
        route("chev", YELLOW, GREEN, RED)),
    "route-shunting-release": ("Shunting route release",
        "Chevrons, releasing.",
        route("chev", MAGENTA, MAGENTA, MAGENTA)),
    "train-approved": ("Train approved",
        "Number on the run in from the layout edge.",
        life(GREY, 20, RED, 6, "in")),
    "train-advance": ("Train advance",
        "One step of the lifecycle: the number moves on past the signal. Which step that "
        "turns out to be - arrived, departed, or gone from the station - is deduced from "
        "where the number is standing, so the key says only 'onwards'.",
        life(GREEN, 76, RED, 90, "out")),
    "area-release": ("Shunting area released",
        "Two turnouts, blue (6.2.12): the shunter has them.",
        fan(BLUE) + padlock(False)),
    "area-lock": ("Shunting area taken back",
        "Taken back and held against the shunter.",
        fan(GREY) + padlock(True)),
    "point-lock": ("Point lock", "A point held by hand.", small_point() + padlock(True)),
    "point-unlock": ("Point unlock", "The hold taken off.", small_point() + padlock(False)),
    "signal-lock-stop": ("Signal lock at stop",
        "A signal held at stop against route logic.",
        line(2, 72, 18, 72, GREY, THIN) + tri(60, 72, GREY) + padlock(True)),
    "signal-unlock-stop": ("Signal unlock stop",
        "The hold taken off.",
        line(2, 72, 18, 72, GREY, THIN) + tri(60, 72, GREY) + padlock(False)),
    "buttons-alternate": ("Switch to alternate buttons",
        "You are on the grey set; the press goes to white.",
        cards(GREY, WHITE)),
    "buttons-main": ("Switch to main buttons",
        "You are on the white set; the press comes back.",
        cards(WHITE, GREY)),
}

def bar(x, y, w, h, r, colour=WHITE):
    return f'<rect x="{n(x)}" y="{n(y)}" width="{n(w)}" height="{n(h)}" rx="{n(r)}" fill="{colour}"/>'


NUMERIC = {
    "key-undo": ("Undo one press",
        "The minus key. Drawn rather than set in type: the true minus sign is missing from "
        "Bahnschrift, and a hyphen at key size reads as a speck.",
        bar(20, 42, 56, 12, 6)),
    "key-plus": ("Straight, and the affirmative",
        "The plus key. Drawn rather than set in type for the same reason its arms are the "
        "minus bar crossed: a typeset plus is set to the height of a digit and comes out "
        "visibly smaller than the drawn keys either side of it.",
        bar(20, 42, 56, 12, 6) + bar(42, 20, 12, 56, 6)),
    "key-all": ("All of them",
        "The wildcard. Eight spokes, as the specification spells it, because no face here "
        "carries the eight-spoked asterisk.",
        '<g stroke="#FFFFFF" stroke-width="9" stroke-linecap="round">'
        '<line x1="48" y1="20" x2="48" y2="76"/><line x1="20" y1="48" x2="76" y2="48"/>'
        '<line x1="28" y1="28" x2="68" y2="68"/><line x1="68" y1="28" x2="28" y2="68"/></g>'),
    "key-send": ("Send",
        "The enter key: the carriage-return arrow, which no face here carries either.",
        '<path d="M72,24 V46 a8 8 0 0 1 -8 8 H32" fill="none" stroke="#FFFFFF" stroke-width="9" '
        'stroke-linecap="round" stroke-linejoin="round"/>'
        '<polygon points="38,38 38,70 20,54" fill="#FFFFFF"/>'),
}
ICONS.update(NUMERIC)

for name, (title, desc, body) in ICONS.items():
    svg = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96" width="96" height="96">\n'
           f'  <title>{title}</title>\n'
           f'  <!-- {desc} -->\n'
           f'  {body}\n</svg>\n')
    io.open(os.path.join(OUT, name + ".svg"), "w", encoding="utf-8", newline="\n").write(svg)

print(f"{len(ICONS)} icons, signal symbol {n(S)} units everywhere")
print(f"  standalone signal  {n(SIG_TIP-S)}..{n(SIG_TIP)}")
print(f"  route signals      {n(FROM_TIP-S)}..{n(FROM_TIP)} and {n(TO_TIP-S)}..{n(TO_TIP)}, link {n(BAR_A)}..{n(BAR_B)}")
print(f"  train signal       {n(LIFE_TIP-S)}..{n(LIFE_TIP)}")
print(f"  lock signal        {n(60-S)}..60")
