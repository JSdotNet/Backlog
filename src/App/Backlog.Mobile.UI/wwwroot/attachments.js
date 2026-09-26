// Opens one of the talk note's hidden file inputs and reports back when the
// dialog closes with nothing chosen. A choice arrives through the input's own
// change event, which Blazor's InputFile already listens to.
export function open(input, picker) {
    if (!input) {
        throw new Error("The file input is not on the page.");
    }

    input.addEventListener("cancel", () => picker.invokeMethodAsync("OnCancelled"), { once: true });

    // A fresh pick of the same file still counts as a change.
    input.value = "";
    input.click();
}
