# gzip-полезная нагрузка: читается любым rpm, в том числе на RedOS
%define _binary_payload w9.gzdio
%define _source_payload w9.gzdio

Name:           boardswitcher
Version:        1.0
Release:        5%{?dist}
Summary:        Layout switcher for the last typed word and selected text (Caps Lock)
License:        MIT
BuildArch:      noarch
Source0:        %{name}-%{version}.tar.gz

Requires:       python3
Requires:       python3-xlib
Requires:       xclip
Requires:       libX11
Suggests:       python3-qt5

%description
Minimal Punto Switcher analogue for X11 (KDE Plasma 5). Caps Lock converts the
last typed word, or the selected text, to the other keyboard layout and sets the
system layout accordingly. Started automatically at KDE login as the user.
The system keyboard settings must have caps:none and must not have
grp:caps_toggle (see the README).

%prep
%setup -q

%build
# nothing to build

%install
install -Dm0755 boardswitcher %{buildroot}%{_bindir}/boardswitcher
install -Dm0644 boardswitcher.desktop %{buildroot}%{_sysconfdir}/xdg/autostart/boardswitcher.desktop
install -Dm0644 boardswitcher-menu.desktop %{buildroot}%{_datadir}/applications/boardswitcher.desktop

%files
%license LICENSE
%doc README.md
%{_bindir}/boardswitcher
%config(noreplace) %{_sysconfdir}/xdg/autostart/boardswitcher.desktop
%{_datadir}/applications/boardswitcher.desktop

%changelog
* Wed Oct 21 2026 Dmitrii Markov <skomoroker@gmail.com> - 1.0-5
- Add LICENSE (MIT) and README; set author

* Wed Oct 21 2026 Dmitrii Markov <skomoroker@gmail.com> - 1.0-4
- Add application menu entry (Office category)

* Wed Oct 21 2026 Dmitrii Markov <skomoroker@gmail.com> - 1.0-3
- Requires caps:none (Caps Lock lock disabled in system settings); removed Caps Lock undo and release wait
- Whitespace-only selection no longer pasted (no extra newline)

* Wed Oct 21 2026 Dmitrii Markov <skomoroker@gmail.com> - 1.0-2
- Fix: Caps Lock no longer stays on when the key is held (wait for physical release before undoing)

* Wed Oct 21 2026 Dmitrii Markov <skomoroker@gmail.com> - 1.0-1
- Initial package
