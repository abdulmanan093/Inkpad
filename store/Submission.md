# Microsoft Store submission draft

**App name** — Inkpad, subject to Partner Center availability. **QuillTabs** is the fallback to check if Inkpad is unavailable or too easy to confuse with Inkpad Notepad on mobile.

**Product type** — MSIX app for Windows desktop, x64. The package targets Windows 10 version 2004 or later and Windows 11. Set the base price to **Free**, with no trial or add-ons.

## Store description

Open local text files, write notes, and sketch ideas in one tabbed Windows editor. Inkpad works offline and saves files where you choose. Use ordinary `.txt` files for plain text or save an `.inknote` to keep rich text, drawings, tables, and colored checkboxes together. There is no Inkpad account or subscription.

## Product features

- Edit local text files in tabs.
- Draw freehand and add lines, arrows, rectangles, ellipses, and triangles.
- Insert clickable checkboxes, choose custom colors, and continue checklists with Enter.
- Add editable tables, headings, and links.
- Highlight text and paste, resize, or crop images in a note.
- Keep formatted notes and drawings together in an `.inknote` file.

## Certification notes

Inkpad is a classic WPF desktop editor. The package uses `runFullTrust` to run its WPF executable in the signed-in user's context and to open and save files the user selects. It requires neither administrator privileges nor a network connection for editing, drawing, or local file storage. Links open in the user's default browser only when requested. The package declares the `.inknote` file type.

To test, type a note, add a checkbox, and draw a line on the same page. Save as `.inknote`, close the note, reopen it, and verify that the checkbox and drawing are still present. Repeat with the device offline. For plain-text testing, open a `.txt` file, edit it, and save it.

## Privacy policy draft

Inkpad stores notes on your device in the locations you choose. It reads and writes the files you open or save and stores preferences, recent file paths, and window placement on your device. It does not require an account, collect analytics, or send your notes to the developer. If you open a web link from a note, the browser opens the destination under its own privacy settings. The Microsoft Store handles installation and updates under Microsoft's policies.

Before submitting, publish this privacy text at a publicly accessible URL and add the publisher's support contact. Review the privacy answers and age rating questionnaire in Partner Center.

## Build the submission package

In Partner Center, open **Apps and games → New product → MSIX or PWA app**. Check and reserve the chosen name. Copy **Package/Identity/Name**, **Package/Identity/Publisher**, and **Publisher display name** from **Product management → Product identity**. Use those exact values in the build command.

```powershell
.\store\Build-Msix.ps1 -IdentityName '<package identity name>' `
    -Publisher '<package publisher>' -PublisherDisplayName '<publisher display name>' `
    -AppName 'Inkpad'
```

Upload the generated `store\Inkpad.msix` to the **Packages** section. Add the English description, a screenshot, support information, and the privacy policy. Set the price to Free, complete the age rating, and submit for Microsoft certification.

`-ValidationOnly` builds with a local test identity to check the package layout and manifest. Use the Partner Center identity for the actual upload.
