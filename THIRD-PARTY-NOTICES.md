# Third-Party Notices

Envoy (Copyright (C) 2026 LXB Studio LLC) is licensed under AGPL-3.0 (see
[LICENSE](LICENSE)). It bundles the third-party components below. Test-only
packages are listed separately and are **not** distributed with the app.

## NuGet packages (shipped in the application)

| Package | Version | License |
|---|---|---|
| HandyControl | 3.5.1 | MIT |
| Microsoft.EntityFrameworkCore.Sqlite | 8.0.0 | MIT |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | MIT |
| Microsoft.Extensions.Hosting | 8.0.1 | MIT |
| Microsoft.Extensions.Http | 8.0.0 | MIT |
| Microsoft.Extensions.Logging | 8.0.0 | MIT |
| Microsoft.Extensions.Logging.Abstractions | 8.0.1 | MIT |
| System.Management | 8.0.0 | MIT |
| System.Security.Cryptography.ProtectedData | 8.0.0 | MIT |
| OllamaSharp | 5.4.25 | MIT |
| PdfPig (UglyToad.PdfPig) | 0.1.8 | Apache-2.0 |
| QuestPDF | 2024.3.0 | QuestPDF Community License (see note) |

**QuestPDF note:** QuestPDF is dual-licensed. The free **Community** license
covers organizations with under US $1M annual gross revenue and open-source
projects; larger organizations require a paid license. Anyone forking Envoy
should confirm their own eligibility; see https://www.questpdf.com/license/ .

### Build-time and test-only packages (NOT shipped with the application)

Microsoft.EntityFrameworkCore.Design 8.0.0 (MIT, build-time only),
xunit 2.6.6 (Apache-2.0), xunit.runner.visualstudio 2.5.6 (Apache-2.0 / MIT),
Moq 4.20.70 (BSD-3-Clause), Microsoft.NET.Test.Sdk 17.8.0 (MIT).

## Bundled fonts

Envoy embeds the following fonts, each licensed under the SIL Open Font
License, Version 1.1 (full text below). Per-font copies also live in
`src/Envoy.UI/Fonts/`.

- **JetBrains Mono**, Copyright 2020 The JetBrains Mono Project Authors
  (https://github.com/JetBrains/JetBrainsMono)
- **Orbitron**, Copyright 2018 The Orbitron Project Authors
  (https://github.com/theleagueof/orbitron), with Reserved Font Name: "Orbitron"

---

## SIL Open Font License, Version 1.1

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
https://openfontlicense.org


-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded, 
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created
using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
