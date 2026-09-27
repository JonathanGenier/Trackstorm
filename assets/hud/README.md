# Shared Item HUD artwork

`ItemAssembly.png` is a 2172×724 RGBA blank chassis derived with the built-in imagegen tool from the user-approved TS-222 reference. All baked dynamic content was removed; the metal, skull divider, gauge ticks, and dark well interiors are the shared substrate. Source/output SHA-256 values are in `sources.json`. The original concept remains user-supplied, not a new external asset acquisition. No separate third-party license is asserted.

The runtime renders this substrate at 600×200 design pixels under the existing uniform HUD scaling. Speed/unit labels occupy the left well. Both `ItemHudSlot` instances share the same 112×80 content arrangement within the two right wells. Slot contents, selection, numeric values and meters are native dynamic UI; there is no screenshot overlay containing baked item identities or numbers. The alpha silhouette preserves the arena around all spikes. Resource-bearing slots use the reference's icon / substantial segmented bar / numeric readout arrangement. Existing registry icons and percentage/count semantics remain in place until their respective item-specific Stories.

`ItemAssembly.gdshader` masks the authored red cells/ticks in source-pixel coordinates around (658, 546), inside radius 225–325, and dims the portion beyond the existing normalized speed fraction. The surrounding chassis and both item wells are excluded. Native render checks exercise 0/100/200 km/h and resource meter pixel depletion/isolation.

## Generation record

Tool: built-in `image_gen.imagegen`, reference-guided precise-object edit, `transparent_background=true`. No CLI/API fallback. Generated output was copied unchanged to `assets/hud/ItemAssembly.png`.

Input: `ChatGPT Image Sep 27, 2026, 01_24_38 PM.png`, supplied and approved by the user.

Exact prompt:

> Use case: precise-object-edit / game UI production asset. Input image is the approved HUD design; preserve its appearance with extremely high fidelity, not a reinterpretation. Create a clean transparent PNG of the same complete continuous metal HUD frame, tightly centered in a wide 3:1 canvas with only a small transparent margin around every spike. Keep exact left semicircular gauge and two right trapezoidal item wells, thick distressed beveled gunmetal rails, rust-orange edge wear, exact spikes and rivets, bolted lower junctions, and central metal skull divider. Keep original dark scratched steel inside all three wells opaque. Preserve the gauge's thin red arc cells and radial red ticks. Remove ONLY all example dynamic content: large '2', 'km/h', blue flame, bullets, both rectangular resource meters including black outlines, '78%' and '320/500'. Inpaint those removed areas with matching uninterrupted dark scratched steel. Keep thin horizontal rule inside the speedometer. Absolutely no numbers, words, icons, bars, new decorations, drop shadow, glow outside the silhouette, or black rectangular background. Keep materials and geometry at least 95% identical to the reference. This is a reusable blank UI substrate; runtime will overlay every item and all values. Make no artistic redesign.

The prompt's fidelity percentage is a generation target, not a computed similarity metric. Runtime comparison and limitations are recorded separately in the TS-222 verification report.
