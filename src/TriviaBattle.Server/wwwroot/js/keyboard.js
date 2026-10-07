// Custom On-Screen Keyboard - Aggressive Android Keyboard Blocking

class OnScreenKeyboard {
    constructor() {
        this.activeInput = null;
        this.keyboardElement = null;
        this.init();
    }

    init() {
        // Create keyboard HTML
        this.createKeyboard();

        // Detect if device has touch capability
        const isTouchDevice = ('ontouchstart' in window) || (navigator.maxTouchPoints > 0);

        if (isTouchDevice) {
            // TOUCH DEVICES (Android tablets) - Block native keyboard completely
            console.log('Touch device detected - blocking native keyboard');

            // Aggressively set all keyboard-input fields to readonly
            const makeInputsReadonly = () => {
                document.querySelectorAll('.keyboard-input, .pin-input').forEach(input => {
                    input.setAttribute('readonly', 'readonly');
                    input.setAttribute('inputmode', 'none');
                    input.style.caretColor = 'transparent';

                    // Remove any conflicting attributes
                    input.removeAttribute('type');
                    input.setAttribute('type', 'text');
                });
            };

            // Apply immediately
            makeInputsReadonly();

            // Keep applying on any DOM changes
            const observer = new MutationObserver(makeInputsReadonly);
            observer.observe(document.body, { childList: true, subtree: true });

            // Prevent ALL focus attempts on keyboard inputs
            document.addEventListener('focusin', (e) => {
                if (e.target.matches('.keyboard-input, .pin-input, #pinInput')) {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    e.target.blur();
                    return false;
                }
            }, true);

            document.addEventListener('focus', (e) => {
                if (e.target.matches('.keyboard-input, .pin-input, #pinInput')) {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    e.target.blur();
                    return false;
                }
            }, true);

            // Prevent touchstart from triggering focus
            document.addEventListener('touchstart', (e) => {
                if (e.target.matches('.keyboard-input, .pin-input, #pinInput')) {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    return false;
                }
            }, { passive: false, capture: true });

            // Show custom keyboard on touchend
            document.addEventListener('touchend', (e) => {
                if (e.target.matches('.keyboard-input, .pin-input, #pinInput')) {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    this.showKeyboard(e.target);
                    return false;
                }
            }, { passive: false, capture: true });

            // Also handle click events
            document.addEventListener('click', (e) => {
                if (e.target.matches('.keyboard-input, .pin-input, #pinInput')) {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    this.showKeyboard(e.target);
                    return false;
                }
            }, true);

        } else {
            // DESKTOP/PC - Allow normal keyboard input
            console.log('Desktop detected - allowing normal keyboard + custom keyboard option');

            // Show custom keyboard when clicking the input
            document.addEventListener('click', (e) => {
                if (e.target.matches('.keyboard-input, .pin-input')) {
                    this.showKeyboard(e.target);
                }
            });

            // Allow normal focus and typing
            document.addEventListener('focus', (e) => {
                if (e.target.matches('.keyboard-input, .pin-input')) {
                    this.activeInput = e.target;
                    this.showKeyboard(e.target);
                }
            });

            // Update display when user types with physical keyboard
            document.addEventListener('input', (e) => {
                if (e.target.matches('.keyboard-input, .pin-input') && this.activeInput === e.target) {
                    const display = this.keyboardElement.querySelector('.keyboard-display-input');
                    if (display) display.value = e.target.value;
                }
            });
        }
    }

    createKeyboard() {
        const keyboardHTML = `
            <div id="onScreenKeyboard" class="on-screen-keyboard hidden">
                <div class="keyboard-header">
                    <button class="keyboard-close" onclick="onScreenKeyboard.hideKeyboard()">✕</button>
                </div>
                <div class="keyboard-display">
                    <input type="text" class="keyboard-display-input" readonly>
                </div>
                <div class="keyboard-keys">
                    <!-- Number Keypad -->
                    <div id="numberKeys" class="number-keys">
                        <div class="keyboard-row">
                            <button class="key" data-key="1">1</button>
                            <button class="key" data-key="2">2</button>
                            <button class="key" data-key="3">3</button>
                        </div>
                        <div class="keyboard-row">
                            <button class="key" data-key="4">4</button>
                            <button class="key" data-key="5">5</button>
                            <button class="key" data-key="6">6</button>
                        </div>
                        <div class="keyboard-row">
                            <button class="key" data-key="7">7</button>
                            <button class="key" data-key="8">8</button>
                            <button class="key" data-key="9">9</button>
                        </div>
                        <div class="keyboard-row">
                            <button class="key key-wide" data-key="clear">Clear</button>
                            <button class="key" data-key="0">0</button>
                            <button class="key key-wide key-backspace" data-key="backspace">⌫</button>
                        </div>
                    </div>
                    
                    <!-- Letter Keyboard -->
                    <div id="letterKeys" class="letter-keys hidden">
                        <div class="keyboard-row">
                            <button class="key" data-key="Q">Q</button>
                            <button class="key" data-key="W">W</button>
                            <button class="key" data-key="E">E</button>
                            <button class="key" data-key="R">R</button>
                            <button class="key" data-key="T">T</button>
                            <button class="key" data-key="Y">Y</button>
                            <button class="key" data-key="U">U</button>
                            <button class="key" data-key="I">I</button>
                            <button class="key" data-key="O">O</button>
                            <button class="key" data-key="P">P</button>
                        </div>
                        <div class="keyboard-row">
                            <button class="key" data-key="A">A</button>
                            <button class="key" data-key="S">S</button>
                            <button class="key" data-key="D">D</button>
                            <button class="key" data-key="F">F</button>
                            <button class="key" data-key="G">G</button>
                            <button class="key" data-key="H">H</button>
                            <button class="key" data-key="J">J</button>
                            <button class="key" data-key="K">K</button>
                            <button class="key" data-key="L">L</button>
                        </div>
                        <div class="keyboard-row">
                            <button class="key" data-key="Z">Z</button>
                            <button class="key" data-key="X">X</button>
                            <button class="key" data-key="C">C</button>
                            <button class="key" data-key="V">V</button>
                            <button class="key" data-key="B">B</button>
                            <button class="key" data-key="N">N</button>
                            <button class="key" data-key="M">M</button>
                            <button class="key key-wide key-backspace" data-key="backspace">⌫</button>
                        </div>
                        <div class="keyboard-row">
                            <button class="key key-space" data-key=" ">Space</button>
                            <button class="key key-wide" data-key="clear">Clear</button>
                        </div>
                    </div>
                    
                    <!-- Action Buttons -->
                    <div class="keyboard-row">
                        <button class="key key-toggle" onclick="onScreenKeyboard.toggleKeyboard()">
                            <span id="toggleLabel">ABC</span>
                        </button>
                        <button class="key key-action" onclick="onScreenKeyboard.submitAndClose()">Done</button>
                    </div>
                </div>
            </div>
        `;

        // Add to body
        document.body.insertAdjacentHTML('beforeend', keyboardHTML);
        this.keyboardElement = document.getElementById('onScreenKeyboard');

        // Add click handlers to keys
        this.keyboardElement.querySelectorAll('.key').forEach(key => {
            key.addEventListener('click', (e) => {
                e.preventDefault();
                const value = e.target.getAttribute('data-key');
                if (value) {
                    this.handleKeyPress(value);
                }
            });
        });
    }

    showKeyboard(inputElement) {
        this.activeInput = inputElement;
        this.keyboardElement.classList.remove('hidden');

        // Add class to body to adjust layout
        document.body.classList.add('keyboard-active');

        // Update display
        const display = this.keyboardElement.querySelector('.keyboard-display-input');
        display.value = inputElement.value;

        // Determine which keyboard to show
        const numberKeys = document.getElementById('numberKeys');
        const letterKeys = document.getElementById('letterKeys');
        const toggleLabel = document.getElementById('toggleLabel');
        const toggleBtn = this.keyboardElement.querySelector('.key-toggle');

        const isPINInput = inputElement.classList.contains('pin-input') || inputElement.id === 'pinInput';

        if (isPINInput) {
            // PIN input - show numbers only
            numberKeys.classList.remove('hidden');
            letterKeys.classList.add('hidden');
            toggleLabel.textContent = 'ABC';
        } else {
            // Text input - start with letters
            numberKeys.classList.add('hidden');
            letterKeys.classList.remove('hidden');
            toggleLabel.textContent = '123';
            toggleBtn.style.display = 'flex';
        }
    }

    hideKeyboard() {
        if (this.activeInput) {
            // Trigger change event
            this.activeInput.dispatchEvent(new Event('input', { bubbles: true }));
            this.activeInput.dispatchEvent(new Event('change', { bubbles: true }));
        }

        this.keyboardElement.classList.add('hidden');
        document.body.classList.remove('keyboard-active');
        this.activeInput = null;
    }

    handleKeyPress(key) {
        if (!this.activeInput) return;

        const display = this.keyboardElement.querySelector('.keyboard-display-input');

        switch (key) {
            case 'backspace':
                this.activeInput.value = this.activeInput.value.slice(0, -1);
                break;
            case 'clear':
                this.activeInput.value = '';
                break;
            default:
                const maxLength = this.activeInput.getAttribute('maxlength');
                if (!maxLength || this.activeInput.value.length < parseInt(maxLength)) {
                    this.activeInput.value += key;
                }
                break;
        }

        display.value = this.activeInput.value;
        this.activeInput.dispatchEvent(new Event('input', { bubbles: true }));
    }

    toggleKeyboard() {
        const numberKeys = document.getElementById('numberKeys');
        const letterKeys = document.getElementById('letterKeys');
        const toggleLabel = document.getElementById('toggleLabel');

        if (numberKeys.classList.contains('hidden')) {
            numberKeys.classList.remove('hidden');
            letterKeys.classList.add('hidden');
            toggleLabel.textContent = 'ABC';
        } else {
            numberKeys.classList.add('hidden');
            letterKeys.classList.remove('hidden');
            toggleLabel.textContent = '123';
        }
    }

    submitAndClose() {
        if (this.activeInput) {
            this.activeInput.dispatchEvent(new Event('change', { bubbles: true }));
        }
        this.hideKeyboard();
    }
}

// Initialize keyboard IMMEDIATELY - don't wait for DOMContentLoaded
let onScreenKeyboard;

// Run immediately if DOM is already loaded
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        onScreenKeyboard = new OnScreenKeyboard();
    });
} else {
    // DOM already loaded
    onScreenKeyboard = new OnScreenKeyboard();
}