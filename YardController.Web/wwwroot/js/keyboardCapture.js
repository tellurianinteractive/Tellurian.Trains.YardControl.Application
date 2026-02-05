let dotNetHelper = null;
let keydownHandler = null;

export function initializeKeyboardCapture(helper) {
    dotNetHelper = helper;

    keydownHandler = (e) => {
        if (isRelevantKey(e)) {
            e.preventDefault();
            dotNetHelper.invokeMethodAsync('OnKeyPressed', {
                keyChar: e.key.length === 1 ? e.key : '',
                key: mapKeyCode(e.code),
                modifiers: getModifiers(e)
            });
        }
    };

    document.addEventListener('keydown', keydownHandler);
}

export function disposeKeyboardCapture() {
    if (keydownHandler) {
        document.removeEventListener('keydown', keydownHandler);
        keydownHandler = null;
    }
    dotNetHelper = null;
}

function isRelevantKey(e) {
    // Accept numpad keys, digit keys, and special operator keys
    const code = e.code;
    return code.startsWith('Numpad') ||
           code.startsWith('Digit') ||
           code === 'Enter' ||
           code === 'Backspace' ||
           code === 'NumpadEnter' ||
           code === 'NumpadAdd' ||
           code === 'NumpadSubtract' ||
           code === 'NumpadMultiply' ||
           code === 'NumpadDivide' ||
           code === 'NumpadDecimal' ||
           code === 'Period' ||
           code === 'Slash' ||
           code === 'Minus' ||
           code === 'Equal';
}

function mapKeyCode(code) {
    const map = {
        'Numpad0': 96,      // ConsoleKey.NumPad0
        'Numpad1': 97,
        'Numpad2': 98,
        'Numpad3': 99,
        'Numpad4': 100,
        'Numpad5': 101,
        'Numpad6': 102,
        'Numpad7': 103,
        'Numpad8': 104,
        'Numpad9': 105,
        'Digit0': 48,       // ConsoleKey.D0
        'Digit1': 49,
        'Digit2': 50,
        'Digit3': 51,
        'Digit4': 52,
        'Digit5': 53,
        'Digit6': 54,
        'Digit7': 55,
        'Digit8': 56,
        'Digit9': 57,
        'NumpadEnter': 13,  // ConsoleKey.Enter
        'Enter': 13,
        'NumpadAdd': 107,   // ConsoleKey.Add
        'NumpadSubtract': 109, // ConsoleKey.Subtract
        'Minus': 109,
        'NumpadMultiply': 106, // ConsoleKey.Multiply
        'NumpadDivide': 111,   // ConsoleKey.Divide
        'Slash': 111,
        'NumpadDecimal': 110,  // ConsoleKey.Decimal
        'Period': 110,
        'Backspace': 8,        // ConsoleKey.Backspace
        'Equal': 187           // Map to Enter for train route set command
    };
    return map[code] || 0;
}

function getModifiers(e) {
    let modifiers = 0;
    if (e.shiftKey) modifiers |= 1;
    if (e.altKey) modifiers |= 2;
    if (e.ctrlKey) modifiers |= 4;
    return modifiers;
}
