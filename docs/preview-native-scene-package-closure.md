# Preview native scene package closure

At LibreWPF `6b77f3cdd29d551907acd5636d75684f3433f9da`,
[Build 36936894264, SDK job 110632615136](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36936894264/job/110632615136)
failed during the extracted preview bundle's `BundleSdkSmoke` restore:
`NU1102`, missing `ProGPU.Scene.Native >= 0.1.0-source.48a49afe`.
The selected local feed had no version of that package. Bundle verification had
already passed because it checked the same incomplete package inventory.

The WPF bridge already declared the correct dependency. The preview inventory,
SDK pack/stage calls and private package snapshot omitted it. Repair commit
`7e394d1b4dbadaded21973ccb4409aa894f60a32` adds the existing ProGPU project at
the same selected immutable version, shares snapshot enumeration with the
central inventory, and checks internal nuspec dependency closure in both package
audit and extracted-bundle verification. No source dependency pin, native payload
or version fallback changes. Runtime/payload/provenance checks, generated MIL
verification and actual package SDK/application smoke remain required.

The acceptance action is restoring the unchanged bundle SDK consumer; package
delivery to ShowcaseApp depends on the same closure. A metadata fixture does not
qualify that application or the complete hosted package workflow.

After the implementation commit, the 16 offline controls in
`eng/tests/test_preview_package_closure.py` passed in 0.147 seconds. They exercise
metadata-only test ZIPs, exact source-version closure, the original omission,
missing transitive dependencies, mismatched versions, malformed metadata, read
limits, actual package-list/pack-call correspondence and the product snapshot
function's byte-preserving copy. Changed shell scripts passed `bash -n`; Git's
whitespace check passed. No restore, managed/native build, native runtime
download/staging, GPU/VM run or hosted retry was performed for this receipt.

The original failing CI remains evidence, not a passing receipt. Root integration
and a new exact-head hosted SDK/package run are still required.
