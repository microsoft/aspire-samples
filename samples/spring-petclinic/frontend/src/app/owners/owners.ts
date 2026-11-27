import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { ClinicApi, toProblem } from '../clinic-api';
import { NewOwner, Owner, Problem, speciesGlyph } from '../models';

interface Registration extends NewOwner {
  petName: string;
  petType: string;
  petBirthDate: string;
}

@Component({
  selector: 'app-owners',
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './owners.html',
  styleUrl: './owners.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OwnersPanel {
  private readonly api = inject(ClinicApi);

  readonly owners = input.required<Owner[]>();
  readonly registered = output<Owner>();

  protected readonly species = ['cat', 'dog', 'bird', 'hamster', 'lizard', 'snake', 'rabbit'];
  protected readonly glyph = speciesGlyph;
  protected readonly query = signal('');
  protected readonly formOpen = signal(false);
  protected readonly saving = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly lastRegisteredId = signal<number | null>(null);
  protected registration: Registration = this.empty();

  protected readonly filtered = computed(() => {
    const query = this.query().trim().toLowerCase();
    return query
      ? this.owners().filter((owner) =>
          [owner.firstName, owner.lastName, owner.city, ...owner.pets.map((p) => p.name)].some(
            (value) => value.toLowerCase().includes(query),
          ),
        )
      : this.owners();
  });

  protected toggleForm(): void {
    this.formOpen.update((open) => !open);
    this.problem.set(null);
  }

  protected register(form: NgForm): void {
    if (form.invalid || this.saving()) {
      return;
    }

    const { petName, petType, petBirthDate, ...owner } = this.registration;
    const pet = petName.trim()
      ? { name: petName.trim(), type: petType, birthDate: petBirthDate }
      : null;
    this.saving.set(true);
    this.problem.set(null);
    this.api.registerOwner(owner, pet).subscribe({
      next: (created) => {
        this.saving.set(false);
        this.lastRegisteredId.set(created.id);
        this.registered.emit(created);
        this.registration = this.empty();
        form.resetForm(this.registration);
        this.formOpen.set(false);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.problem.set(toProblem(error));
      },
    });
  }

  private empty(): Registration {
    return {
      firstName: '',
      lastName: '',
      address: '',
      city: '',
      telephone: '',
      petName: '',
      petType: 'dog',
      petBirthDate: '',
    };
  }
}
