namespace CustomHideoutRadio
{
    // Permit only one explicit context-menu group operation at a time.
    public sealed class LinkedSwitchGate
    {
        private bool busy;
        public bool TryEnter(bool enabled, bool wholeHideout, bool radio)
        {
            if (busy || !enabled || !wholeHideout || !radio) return false;
            busy = true;
            return true;
        }
        public void Exit() { busy = false; }
    }
}
