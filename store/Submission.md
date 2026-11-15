# NoteMix Pad Microsoft Store submission

**App name** — NoteMix Pad

**Product type** — MSIX app for Windows desktop, x64. The package targets Windows 10 version 2004 or later and Windows 11. Set the base price to **Free**, with no trial or add-ons.

## Upload in Partner Center

1. Open **Apps and games → NoteMix Pad** and open the submission draft.
2. Under **Packages**, upload `NoteMix Pad 1.10.4 x64.msix` from the build below. If the draft contains an earlier x64 package, replace it with this one.
3. Under **Store listings**, add the description below and upload the [desktop screenshot](../assets/inkpad.png).
4. Complete **Properties**, **Age ratings**, and **Pricing and availability**. Add the publisher’s support contact and privacy policy URL as applicable. Submit for certification when Partner Center enables that action.

[Microsoft guide to uploading packages](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/upload-app-packages?pivots=store-installer-msix)

## Store description

Open local text files, write notes, and sketch ideas in one tabbed Windows editor. NoteMix Pad works offline and saves files where you choose. Use ordinary `.txt` files for plain text or save a `.notemix` file to keep rich text, drawings, tables, and colored checkboxes together. The app requires no account or subscription.

## Product features

- Edit local text files in tabs.
- Draw freehand and add lines, arrows, rectangles, ellipses, and triangles.
- Insert clickable checkboxes, choose custom colors, and continue checklists with Enter.
- Add editable tables, headings, and links.
- Highlight text and paste, resize, or crop images in a note.
- Keep formatted notes and drawings together in a `.notemix` file. Open existing `.inknote` files too.

## Certification notes

NoteMix Pad is a classic WPF desktop editor. The package uses `runFullTrust` to run its WPF executable in the signed-in user's context and to open and save files the user selects. It requires neither administrator privileges nor a network connection for editing, drawing, or local file storage. Links open in the user's default browser only when requested. The package registers `.notemix` and the older `.inknote` extension.

To test, type a note, add a checkbox, and draw a line on the same page. Save as `.notemix`, close the note, reopen it, and verify that the checkbox and drawing are still present. Repeat with the device offline. For plain-text testing, open a `.txt` file, edit it, and save it.

## Privacy policy draft

NoteMix Pad stores notes on your device in the locations you choose. It reads and writes the files you open or save and stores preferences, recent file paths, and window placement on your device. It does not require an account, collect analytics, or send your notes to the developer. If you open a web link from a note, the browser opens the destination under its own privacy settings. The Microsoft Store handles installation and updates under Microsoft's policies.

## Build the submission package

Install the Windows SDK with the MSIX packaging tools, then run the following command from the repository root. The identity values come from the NoteMix Pad product in Partner Center.

```powershell
.\store\Build-Msix.ps1 -IdentityName 'abdulmanan.NoteMixPad' `
    -Publisher 'CN=877EE231-8099-4FE5-A759-BCEB8F27DC2D' `
    -PublisherDisplayName 'abdulmanan' `
    -OutputPath '.\store\NoteMix Pad 1.10.4 x64.msix'
```

`-ValidationOnly` builds with a local test identity to check the package layout and manifest. Use the package from the command above for Partner Center.
