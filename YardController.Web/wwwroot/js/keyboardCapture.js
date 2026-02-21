let dotNetHelper = null;
let keydownHandler = null;
let keyupHandler = null;
let altNumpadDigits = '';

export function initializeKeyboardCapture(helper) {
    dotNetHelper = helper;

    keydownHandler = (e) => {
        // Track Alt+numpad sequence (Windows Alt+code input method)
        if (e.code === 'AltLeft' || e.code === 'AltRight') {
            altNumpadDigits = '';
            return;
        }
        if (e.altKey && e.code.startsWith('Numpad') && e.code !== 'NumpadEnter') {
            // Collect the digit from the numpad code (e.g., 'Numpad6' → '6')
            const digit = e.code.replace('Numpad', '');
            if (digit.length === 1 && digit >= '0' && digit <= '9') {
                altNumpadDigits += digit;
                e.preventDefault();
                return;
            }
        }

        // Not an Alt+numpad sequence — reset and handle normally
        altNumpadDigits = '';

        if (isRelevantKey(e)) {
            e.preventDefault();
            const mapped = mapKeyCode(e.code);
            const key = mapped > 0 ? mapped : mapByChar(e.key);
            dotNetHelper.invokeMethodAsync('OnKeyPressed', {
                keyChar: e.key.length === 1 ? e.key : '',
                key: key,
                modifiers: getModifiers(e)
            });
        }
    };

    keyupHandler = (e) => {
        // Alt+numpad sequence completes on Alt release
        if ((e.code === 'AltLeft' || e.code === 'AltRight') && altNumpadDigits.length > 0) {
            const charCode = parseInt(altNumpadDigits, 10);
            altNumpadDigits = '';
            if (charCode > 0 && charCode < 128) {
                const ch = String.fromCharCode(charCode);
                const mapped = mapByChar(ch);
                if (mapped > 0) {
                    e.preventDefault();
                    dotNetHelper.invokeMethodAsync('OnKeyPressed', {
                        keyChar: ch,
                        key: mapped,
                        modifiers: 0
                    });
                }
            }
        }
    };

    document.addEventListener('keydown', keydownHandler);
    document.addEventListener('keyup', keyupHandler);
}

export function disposeKeyboardCapture() {
    if (keydownHandler) {
        document.removeEventListener('keydown', keydownHandler);
        keydownHandler = null;
    }
    if (keyupHandler) {
        document.removeEventListener('keyup', keyupHandler);
        keyupHandler = null;
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
           code === 'Equal' ||
           code === 'Escape' ||
           e.key === '=';
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
        'NumpadEqual': 187,    // ConsoleKey.OemPlus (train number separator)
        'Equal': 187,          // ConsoleKey.OemPlus (train number separator)
        'Escape': 27           // ConsoleKey.Escape (cancel route)
    };
    return map[code] || 0;
}

function mapByChar(key) {
    if (key === '=') return 187; // ConsoleKey.OemPlus
    return 0;
}

function getModifiers(e) {
    let modifiers = 0;
    if (e.shiftKey) modifiers |= 1;
    if (e.altKey) modifiers |= 2;
    if (e.ctrlKey) modifiers |= 4;
    return modifiers;
}
